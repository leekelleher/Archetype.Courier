using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Enums;
using Umbraco.Courier.Core.Helpers;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Archetype.Courier.DataResolvers
{
	public class ArchetypeDataResolver : PropertyDataResolverProvider
	{
		private enum Direction
		{
			Extracting,
			Packaging
		}

		public override string EditorAlias
		{
			get
			{
				return Archetype.Umbraco.Constants.PropertyEditorAlias;
			}
		}

		public override void ExtractingDataType(DataType item)
		{
			ReplaceDataTypeIds(item, Direction.Extracting);
		}

		public override void ExtractingProperty(Item item, ContentProperty propertyData)
		{
			ReplacePropertyDataIds(item, propertyData, Direction.Extracting);
		}

		public override void PackagingDataType(DataType item)
		{
			ReplaceDataTypeIds(item, Direction.Packaging);
		}

		public override void PackagingProperty(Item item, ContentProperty propertyData)
		{
			ReplacePropertyDataIds(item, propertyData, Direction.Packaging);
		}

		private void ReplaceDataTypeIds(DataType item, Direction direction)
		{
			if (item.Prevalues != null && item.Prevalues.Count > 0)
			{
				var prevalue = item.Prevalues[0];
				if (prevalue.Alias.InvariantEquals(Archetype.Umbraco.Constants.PreValueAlias) && !string.IsNullOrWhiteSpace(prevalue.Value))
				{
					var config = JsonConvert.DeserializeObject<Archetype.Umbraco.Models.ArchetypePreValue>(prevalue.Value);

					if (config != null && config.Fieldsets != null)
					{
						foreach (var property in config.Fieldsets.SelectMany(x => x.Properties))
						{
							if (direction == Direction.Packaging)
							{
								var dataTypeGuid = Dependencies.ConvertIdentifier(property.DataTypeId.ToString(), IdentifierReplaceDirection.FromNodeIdToGuid);

								item.Dependencies.Add(dataTypeGuid, ProviderIDCollection.dataTypeItemProviderGuid);

								property.DataTypeGuid = dataTypeGuid;
							}
							else if (direction == Direction.Extracting)
							{
								var identifier = Dependencies.ConvertIdentifier(property.DataTypeGuid, IdentifierReplaceDirection.FromGuidToNodeId);

								int dataTypeId;
								if (int.TryParse(identifier, out dataTypeId))
									property.DataTypeId = dataTypeId;
							}
						}

						item.Prevalues[0].Value = JsonConvert.SerializeObject(config, Formatting.Indented);
					}
				}
			}
		}

		private void ReplacePropertyDataIds(Item item, ContentProperty propertyData, Direction direction)
		{
			if (propertyData.Value != null)
			{
				// just look at the amount of dancing around we have to do in order to fake a `PublishedPropertyType`?!
				var dataTypeId = PersistenceManager.Default.GetNodeId(propertyData.DataType, NodeObjectTypes.DataType);
				var fakePropertyType = this.CreateFakePropertyType(dataTypeId, this.EditorAlias);

				var converter = new Archetype.Umbraco.PropertyConverters.ArchetypeValueConverter();
				var archetype = (Archetype.Umbraco.Models.Archetype)converter.ConvertDataToSource(fakePropertyType, propertyData.Value, false);

				if (archetype != null)
				{
					// create a 'fake' provider, as ultimately only the 'Packaging' enum will be referenced.
					var fakeItemProvider = new PropertyItemProvider();

					foreach (var property in archetype.Fieldsets.SelectMany(x => x.Properties))
					{
						// create a 'fake' item for Courier to process
						var fakeItem = new ContentPropertyData()
						{
							ItemId = item.ItemId,
							Name = string.Format("{0} [{1}: Nested {2} ({3})]", new[] { item.Name, this.EditorAlias, property.PropertyEditorAlias, property.Alias }),
							Data = new List<ContentProperty>
							{
								new ContentProperty
								{
									Alias = property.Alias,
									DataType = PersistenceManager.Default.GetUniqueId(property.DataTypeId, NodeObjectTypes.DataType),
									PropertyEditorAlias = property.PropertyEditorAlias,
									Value = property.Value
								}
							}
						};

						if (direction == Direction.Packaging)
						{
							// run the 'fake' item through Courier's data resolvers
							ResolutionManager.Instance.PackagingItem(fakeItem, fakeItemProvider);

							// pass up the dependencies and resources
							item.Dependencies.AddRange(fakeItem.Dependencies);
							item.Resources.AddRange(fakeItem.Resources);
						}
						else if (direction == Direction.Extracting)
						{
							// run the 'fake' item through Courier's data resolvers
							ResolutionManager.Instance.ExtractingItem(fakeItem, fakeItemProvider);
						}

						// set the resolved property data value
						property.Value = fakeItem.Data.FirstOrDefault().Value;
					}

					if (item.Name.Contains(string.Concat(this.EditorAlias, ": Nested")))
					{
						// if the Archetype is nested, then we only want to return the object itself - not a serialized string
						propertyData.Value = archetype;
					}
					else
					{
						// if the Archetype is the root/container, then we can serialize it to a string
						propertyData.Value = JsonConvert.SerializeObject(archetype, Formatting.Indented);
					}
				}
			}
		}

		private PublishedPropertyType CreateFakePropertyType(int dataTypeId, string propertyEditorAlias)
		{
			return new PublishedPropertyType(null, new PropertyType(new DataTypeDefinition(-1, propertyEditorAlias) { Id = dataTypeId }));
		}
	}
}