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
        private const string SPREADSHEET_URL_START = "https://docs.google.com/spreadsheets/d/";
        private const string SPREADSHEET_URL_END = "/gviz/tq?tqx=out:json&tq&gid=";
        private const string GITHUB_URL_START = "https://raw.githubusercontent.com/tjwogud/Localizations/main/ModLocalizations/";
        private const string GITHUB_URL_END = ".txt";

        private static readonly Type type = typeof(SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>);

        private SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>> localizations = new SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>();
        private readonly Action<string> logger;
        private readonly bool log;
        private readonly string key;
        private readonly int gid;
        private readonly string path;
        public bool Loaded { get; private set; } = false;
        public bool Failed { get; private set; } = false;

        private Localization(string key, int gid, Action<string> logger, bool log, string path, OnLoad onLoad)
        {
            this.key = key;
            this.gid = gid;
            this.logger = logger;
            this.log = log;
            this.path = path;
            if (log)
                logger.Invoke("Loading Localization...");
            StaticCoroutine.Do(Download(onLoad));
        }

        public delegate (string, SerializableDictionary<SystemLanguage, string>) OnLoad((string, SerializableDictionary<SystemLanguage, string>) keyValue);

        public static Localization Load(string key, int gid, UnityModManager.ModEntry modEntry, bool log = true, string path = null, OnLoad onLoad = null)
        {
            return new Localization(key, gid, modEntry.Logger.Log, log, path ?? Path.Combine(modEntry.Path, "localizations.txt"), onLoad);
        }

        public static Localization Load(string key, int gid, string path, Action<string> logger = null, OnLoad onLoad = null)
        {
            return new Localization(key, gid, logger, logger != null, path, onLoad);
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
            if (UnityModManager.HasNetworkConnection())
            {
                LoadFromFile();
                yield break;
            }
            UnityWebRequest request = UnityWebRequest.Get(SPREADSHEET_URL_START + key + SPREADSHEET_URL_END + gid);
            yield return request.SendWebRequest();
            byte[] bytes = request.downloadHandler.data;
            if (bytes == null)
            {
                if (log)
                    logger.Invoke("Couldn't Load Localizations from Sheet, Loading from Github...");
                string modName = new FileInfo(path).DirectoryName;
                request = UnityWebRequest.Get(GITHUB_URL_START + modName + GITHUB_URL_END);
                yield return request.SendWebRequest();
                bytes = request.downloadHandler.data;
                if (bytes == null)
                {
                    Loaded = true;
                    if (log)
                        logger.Invoke("Couldn't Load Localizations!");
                    yield break;
                }
                File.WriteAllBytes(path, bytes);
                if (log)
                    logger.Invoke($"Loaded {localizations.Count} Localizations from Github");
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
                new XmlSerializer(type).Serialize(writer, localizations);
            Loaded = true;
            if (log)
                logger.Invoke($"Loaded {localizations.Count} Localizations from Sheet");
        }

        private void LoadFromFile()
        {
            if (File.Exists(path))
                try
                {
                    using (FileStream fileStream = File.OpenRead(path))
                    {
                        localizations = new XmlSerializer(type).Deserialize(fileStream) as SerializableDictionary<string, SerializableDictionary<SystemLanguage, string>>;
                        Loaded = true;
                        if (log)
                            logger.Invoke($"Loaded {localizations.Count} Localizations from Local File");
                        return;
                    }
                }
                catch (Exception)
                {
                }
            Loaded = true;
            Failed = true;
            if (log)
                logger.Invoke("Couldn't Load Localizations!");
        }
    }
}