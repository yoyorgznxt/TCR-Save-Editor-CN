using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using TCRSaveEditor.Models;

namespace TCRSaveEditor.Services
{
    public static class SaveOrchestrator
    {
        public static void SaveAll(string originalPath, string outputPath, ObservableCollection<City> cities, GameMeta meta)
        {
            string tempPath = outputPath + ".structural-tmp";

            try
            {
                RewriteStructural(originalPath, tempPath, cities, meta);

                var freshCities = new ObservableCollection<City>();
                var freshMeta = new GameMeta();
                GvasReader.LoadSaveFile(tempPath, freshCities, freshMeta);

                CopyScalarEdits(cities, meta, freshCities, freshMeta);

                GvasWriter.WriteChanges(tempPath, outputPath, freshCities, freshMeta);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // ---------- structural pass ----------

        private static void RewriteStructural(string originalPath, string outputPath, ObservableCollection<City> cities, GameMeta meta)
        {
            var (header, topLevel, trailing) = GvasTree.ParseFile(originalPath);

            var savedWorld = GvasTree.FindByName(topLevel, "SavedGlobalWorld")
                ?? throw new InvalidDataException("Couldn't find SavedGlobalWorld");
            GvasTree.OpenStruct(savedWorld);

            var cityAllField = GvasTree.FindByPrefix(savedWorld.StructChildren!, "CityAll")
                ?? throw new InvalidDataException("Couldn't find CityAll inside SavedGlobalWorld");
            GvasTree.OpenStructArray(cityAllField);
            var cityElements = cityAllField.StructArray!.Elements;

            List<PropNode>? universalTemplate = null;

            foreach (var city in cities)
            {
                if (city.Index < 0 || city.Index >= cityElements.Count)
                    throw new InvalidDataException($"City '{city.MapName}' has index {city.Index}, out of range for {cityElements.Count} parsed cities");

                var cityFields = cityElements[city.Index];
                var cargoField = GvasTree.FindByPrefix(cityFields, "CargoItems");
                if (cargoField == null)
                {
                    if (city.Resources.Count > 0)
                        throw new InvalidDataException($"City '{city.MapName}' has resources in the editor but no CargoItems field in the file");
                    continue;
                }

                GvasTree.OpenStructArray(cargoField);
                RebuildResourceArray(cargoField.StructArray!, city.Resources, ref universalTemplate);
            }

            GvasTree.WriteFile(outputPath, header, topLevel, trailing);
        }

        private static void RebuildResourceArray(StructArrayNode arr, ObservableCollection<Resource> uiResources, ref List<PropNode>? universalTemplate)
        {
            var byName = new Dictionary<string, Queue<List<PropNode>>>();
            foreach (var element in arr.Elements)
            {
                var nameNode = GvasTree.FindByType(element, "NameProperty") ?? GvasTree.FindByType(element, "StrProperty");
                if (nameNode == null) continue;

                string name = ReadStringValue(nameNode);
                if (!byName.TryGetValue(name, out var queue))
                {
                    queue = new Queue<List<PropNode>>();
                    byName[name] = queue;
                }
                queue.Enqueue(element);

                universalTemplate ??= element; 
            }

            var rebuilt = new List<List<PropNode>>(uiResources.Count);

            foreach (var resource in uiResources)
            {
                List<PropNode> element;

                if (byName.TryGetValue(resource.Name, out var queue) && queue.Count > 0)
                {
                    element = queue.Dequeue();
                }
                else if (universalTemplate != null)
                {
                    element = GvasTree.CloneElement(universalTemplate);
                }
                else
                {
                    throw new InvalidDataException(
                        $"Can't add new resource '{resource.Name}': no existing name+count element anywhere in the file to use as a structural template");
                }

                var nameNode = GvasTree.FindByType(element, "NameProperty") ?? GvasTree.FindByType(element, "StrProperty");
                var countNode = GvasTree.FindByType(element, "IntProperty");
                if (nameNode == null || countNode == null)
                    throw new InvalidDataException($"Template element for '{resource.Name}' is missing its Name or Count field");

                GvasTree.SetStringValue(nameNode, resource.Name);
                GvasTree.SetIntValue(countNode, resource.Count);

                rebuilt.Add(element);
            }

            arr.Elements = rebuilt;
        }

        private static string ReadStringValue(PropNode node)
        {
            if (node.RawValue.Length == 0) return "";
            return Encoding.ASCII.GetString(node.RawValue, 0, node.RawValue.Length - 1);
        }

        // ---------- scalar copy ----------

        private static void CopyScalarEdits(ObservableCollection<City> cities, GameMeta meta, ObservableCollection<City> freshCities, GameMeta freshMeta)
        {
            var freshByIndex = freshCities.ToDictionary(c => c.Index);

            foreach (var city in cities)
            {
                if (!freshByIndex.TryGetValue(city.Index, out var freshCity))
                    throw new InvalidDataException($"City '{city.MapName}' (index {city.Index}) not found after structural rewrite");

                freshCity.PeoplesCount = city.PeoplesCount;
                freshCity.ReservistsCount = city.ReservistsCount;
            }

            freshMeta.PoliticPoints = meta.PoliticPoints;
            freshMeta.AuthorityPoints = meta.AuthorityPoints;
            freshMeta.StabilityPoints = meta.StabilityPoints;
            freshMeta.DictatorShip = meta.DictatorShip;
            freshMeta.PlayerResearchBonus = meta.PlayerResearchBonus;
            freshMeta.PlayerConstructBonus = meta.PlayerConstructBonus;
        }
    }
}