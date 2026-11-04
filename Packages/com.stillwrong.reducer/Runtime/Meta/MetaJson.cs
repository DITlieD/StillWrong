using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace StillWrong.Meta
{
    public sealed class MetaJson
    {
        private readonly JsonSerializerSettings settings;

        public MetaJson()
        {
            settings = new JsonSerializerSettings();
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            settings.Formatting = Formatting.None;
            settings.NullValueHandling = NullValueHandling.Include;
        }

        public string Serialize<T>(T value)
        {
            return JsonConvert.SerializeObject(value, settings);
        }

        public T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, settings);
        }

        public static string SerializeRaw(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None);
        }

        public static JObject ParseObject(string json)
        {
            return JObject.Parse(json);
        }

        public static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            string quoted = JsonConvert.ToString(value);
            if (quoted.Length >= 2)
            {
                return quoted.Substring(1, quoted.Length - 2);
            }
            return string.Empty;
        }
    }
}
