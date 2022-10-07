using GDMiniJSON;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.Networking;
using UnityModManagerNet;

namespace Localizations
{
    public class Localization : MonoBehaviour
    {
        private static readonly string SPREADSHEET_URL_START = "https://docs.google.com/spreadsheets/d/";
        private static readonly string SPREADSHEET_URL_END = "/gviz/tq?tqx=out:json&tq&gid=";
        private static readonly Type LOCALIZATION_TYPE = typeof(SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>);

        private SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>> localizations = new SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>();
        private readonly UnityModManager.ModEntry modEntry;
        private readonly string key;
        private readonly int gid;
        private readonly string path;
        public bool Loaded { get; private set; } = false;
        public bool Failed { get; private set; } = false;

        private Localization(UnityModManager.ModEntry modEntry, string key, int gid, string path = null, OnLoad onLoad = null)
        {
            this.modEntry = modEntry;
            this.key = key;
            this.gid = gid;
            this.path = path ?? Path.Combine(modEntry.Path, "localizations.txt");
            modEntry.Logger.Log("Loading Localization...");
            StaticCoroutine.Do(Download(onLoad));
        }

        public delegate (string, SerializableDictionary<SystemLanguage, string>) OnLoad((string, SerializableDictionary<SystemLanguage, string>) keyValue);

        public static Localization Load(UnityModManager.ModEntry modEntry, string key, int gid, OnLoad onLoad = null)
        {
            return new Localization(modEntry, key, gid, onLoad: onLoad);
        }

        public string this[string key, SystemLanguage defaultLanguage = SystemLanguage.English, Dictionary<string, object> parameters = null]
        {
            get
            {
                Dictionary<SystemLanguage, string> dict = localizations[key];
                string value = dict.TryGetValue(RDString.language, out string v) ? v : dict[defaultLanguage];
                if (value != null && parameters != null)
                    RDString.ReplaceParameters(value, parameters);
                return value;
            }
        }

        public bool Get(string key, out string value, Dictionary<string, object> parameters = null)
        {
            value = null;
            if (!Loaded || Failed)
                return false;
            value = localizations.TryGetValue(key, out var v)
                ? (v.TryGetValue(RDString.language, out value)
                ? value
                : (v.TryGetValue(SystemLanguage.English, out value)
                ? value
                : (v.Keys.Count != 0 ? v[v.Keys.ToList()[0]]
                : null)))
                : null;
            if (value != null && parameters != null)
                RDString.ReplaceParameters(value, parameters);
            return value != null;
        }

        public Dictionary<SystemLanguage, string> GetAll(string key, Dictionary<string, object> parameters = null)
        {
            return localizations.TryGetValue(key, out SerializableDictionary<SystemLanguage, string> result) ? result.ToDictionary(pair => pair.Key, pair => pair.Value) : null;
        }

        private IEnumerator Download(OnLoad onLoad = null)
        {
            UnityWebRequest request = UnityWebRequest.Get(SPREADSHEET_URL_START + key + SPREADSHEET_URL_END + gid);
            yield return request.SendWebRequest();
            byte[] bytes = request.downloadHandler.data;
            if (bytes == null)
            {
                LoadFromFile();
                yield break;
            }
            string strData = Encoding.UTF8.GetString(bytes);
            strData = strData.Substring(47, strData.Length - 49);
            localizations.Clear();
            var data = ((Json.Deserialize(strData) as Dictionary<string, object>)["table"] as Dictionary<string, object>)["rows"] as List<object>;
            var languageList = ((data.First() as Dictionary<string, object>)["c"] as List<object>).Select(obj =>
                Enum.TryParse<SystemLanguage>((obj as Dictionary<string, object>)?["v"] as string, out var result) ? result : SystemLanguage.Unknown
            ).ToList();
            data.RemoveAt(0);
            languageList.RemoveAt(0);
            foreach (object obj in data)
            {
                List<object> list = (obj as Dictionary<string, object>)["c"] as List<object>;
                string key = (list[0] as Dictionary<string, object>)?["v"] as string;
                if (key.IsNullOrEmpty())
                    continue;
                list.RemoveAt(0);
                var dict = new SerializableDictionary<SystemLanguage, string>();
                for (int i = 0; i < list.Count; i++)
                {
                    SystemLanguage language = languageList[i];
                    if (language == SystemLanguage.Unknown)
                        continue;
                    string value = (list[i] as Dictionary<string, object>)?["v"] as string;
                    if (value.IsNullOrEmpty())
                        continue;
                    dict.Add(language, value.Replace("\\n", "\n"));
                }
                if (onLoad != null)
                {
                    (string, SerializableDictionary<SystemLanguage, string>) keyValue = onLoad((key, dict));
                    key = keyValue.Item1;
                    dict = keyValue.Item2;
                }
                localizations.Add(key, dict);
            }
            using (var writer = new StreamWriter(path))
                new XmlSerializer(LOCALIZATION_TYPE).Serialize(writer, localizations);
            Loaded = true;
            modEntry.Logger.Log($"Loaded {localizations.Count} Localizations from Sheet");
        }

        private void LoadFromFile()
        {
            if (File.Exists(path))
                try
                {
                    using (FileStream fileStream = File.OpenRead(path))
                    {
                        localizations = new XmlSerializer(LOCALIZATION_TYPE).Deserialize(fileStream) as SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>;
                        Loaded = true;
                        modEntry.Logger.Log($"Loaded {localizations.Count} Localizations from Local File");
                        return;
                    }
                }
                catch (Exception)
                {
                }
            Loaded = true;
            Failed = true;
            modEntry.Logger.Log("Couldn't Load Localizations!");
        }
    }
}