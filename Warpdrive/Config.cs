using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Warpdrive
{
    public class Config
    {
        private static Dictionary<string, object> KeyStore = null;
        public static bool Loaded = false;

        public static void Init()
        {
            KeyStore = new Dictionary<string, object>();
        }

        public static void Load(string path = "./config.json")
        {
            var stream = new StreamReader(path);
            try
            {
                var serializer = new JsonSerializer();

                KeyStore = (Dictionary<string, object>)serializer.Deserialize(stream, typeof(Dictionary<string, object>));
                Loaded = true;
            }
            catch
            {
                KeyStore = null;
                Loaded = false;
                throw new Exception("Couldn't load configuration file");
            }
            finally
            {
                stream.Close();
            }
        }

        public static T GetValue<T>(string key)
        {
            if (KeyStore == null || !KeyStore.ContainsKey(key))
                return default(T);

            return (T)Convert.ChangeType(KeyStore[key], typeof(T));
        }

        public static bool GetBool(string key)
        {
            return GetValue<bool>(key);
        }

        public static string GetString(string key)
        {
            return GetValue<string>(key);
        }

        public static int GetInt(string key)
        {
            return GetValue<int>(key);
        }

        public static double GetDouble(string key)
        {
            return GetValue<double>(key);
        }

        public static T[] GetArray<T>(string key)
        {
            return ((JArray)KeyStore[key]).Select(i => i.Value<T>()).ToArray();
        }

        public static bool ContainsPlain(string key, string item)
        {
            return Contains(key, item);
        }

        public static bool Contains(string key, string item)
        {
            if (KeyStore == null || !KeyStore.ContainsKey(key))
                return false;

            var arr = ((JArray)KeyStore[key]).ToArray();

            if (arr.Any())
                return arr.Contains(item);

            return false;
        }

        public static bool Contains(string key, Func<JToken, bool> predicate)
        {
            if (KeyStore == null || !KeyStore.ContainsKey(key))
                return false;

            var arr = ((JArray)KeyStore[key]).ToArray();
            return arr.Any(predicate);
        }

        public static bool Contains(string key, Func<JToken, bool> predicate, out JToken token)
        {
            token = null;

            if (KeyStore == null || !KeyStore.ContainsKey(key))
                return false;

            var arr = ((JArray)KeyStore[key]).ToArray();
            try
            {
                token = arr.First(predicate);
            }
            catch
            {
                return false;
            }

            return true;
        }

        public static void Add(string key, string item)
        {
            if (KeyStore == null)
                return;

            if (!KeyStore.ContainsKey(key))
            {
                KeyStore[key] = new JArray(item);
                return;
            }

            var arr = ((JArray)KeyStore[key]);
            arr.Add(item);
            KeyStore[key] = arr;
        }

        public static bool Remove(string key, JToken item)
        {
            if (KeyStore == null || !KeyStore.ContainsKey(key))
                return false;

            var arr = ((JArray)KeyStore[key]);
            bool success = arr.Remove(item);
            KeyStore[key] = arr;
            return success;
        }

        public static void SetValue(string key, object value)
        {
            KeyStore[key] = value;
        }

        public static void Save(string path = "./config.json")
        {
            string temp = path + ".tmp";

            var stream = new StreamWriter(temp);
            var writer = new JsonTextWriter(stream)
            {
                Indentation = 4,
                Formatting = Formatting.Indented
            };

            try
            {
                var serializer = new JsonSerializer();

                serializer.Serialize(writer, KeyStore);
                File.Replace(temp, path, path + ".bak", true);
            }
            catch
            {
                throw;
            }
            finally
            {
                writer.Close();
                stream.Close();
            }
        }
    }
}
