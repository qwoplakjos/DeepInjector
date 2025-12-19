using System.Collections.Generic;
using System.IO;
using SimpleJSON;

namespace DeepInjector.Models
{
    public class InjectorSettings
    {
        public List<DllEntry> DllEntries { get; set; } = new List<DllEntry>();
        public string LastTargetProcess { get; set; }

        private const string SettingsFileName = "settings.json";

        public static InjectorSettings Load()
        {
            if (File.Exists(SettingsFileName))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFileName);
                    var settings = new InjectorSettings();
                    var jsonNode = JSON.Parse(json);
                    
                    if (jsonNode["LastTargetProcess"] != null)
                        settings.LastTargetProcess = jsonNode["LastTargetProcess"].Value;
                    
                    if (jsonNode["DllEntries"] != null)
                    {
                        var entries = jsonNode["DllEntries"].AsArray;
                        foreach (JSONNode entry in entries)
                        {
                            settings.DllEntries.Add(new DllEntry(
                                entry["Name"].Value,
                                entry["FilePath"].Value
                            ));
                        }
                    }
                    
                    return settings;
                }
                catch
                {
                    return new InjectorSettings();
                }
            }
            return new InjectorSettings();
        }

        public void Save()
        {
            var jsonObject = new JSONObject();
            
            jsonObject["LastTargetProcess"] = LastTargetProcess ?? "";
            
            var entriesArray = new JSONArray();
            foreach (var entry in DllEntries)
            {
                var entryObject = new JSONObject();
                entryObject["Name"] = entry.Name ?? "";
                entryObject["FilePath"] = entry.FilePath ?? "";
                entriesArray.Add(entryObject);
            }
            
            jsonObject["DllEntries"] = entriesArray;
            
            File.WriteAllText(SettingsFileName, jsonObject.ToString(2));
        }
    }
} 