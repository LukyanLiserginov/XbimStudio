using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XplorerPlugin.AiAgent.Settings
{
    /// <summary>
    /// Plugin settings persisted as JSON in %LOCALAPPDATA%\XbimXplorer\ai-agent.settings.json.
    /// The API key is protected with DPAPI (CurrentUser).
    /// </summary>
    public class AgentSettings
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("XbimXplorer.AiAgent.v1");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";

        /// <summary>DPAPI-protected API key (base64). Empty for local endpoints that need no key.</summary>
        [JsonPropertyName("apiKeyProtected")]
        public string ApiKeyProtected { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "gpt-4o-mini";

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.2;

        [JsonPropertyName("maxToolIterations")]
        public int MaxToolIterations { get; set; } = 15;

        [JsonPropertyName("maxRows")]
        public int MaxRows { get; set; } = 50;

        [JsonPropertyName("maxToolResultChars")]
        public int MaxToolResultChars { get; set; } = 20000;

        [JsonPropertyName("reportFolder")]
        public string ReportFolder { get; set; } = "";

        [JsonPropertyName("systemPromptExtra")]
        public string SystemPromptExtra { get; set; } = "";

        [JsonIgnore]
        public string ApiKey
        {
            get => Unprotect(ApiKeyProtected);
            set => ApiKeyProtected = Protect(value);
        }

        [JsonIgnore]
        public string EffectiveReportFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(ReportFolder))
                    return ReportFolder;
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "XbimReports");
            }
        }

        public static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "XbimXplorer", "ai-agent.settings.json");

        public static AgentSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions);
                    if (s != null)
                        return s;
                }
            }
            catch (Exception)
            {
                // fall through to defaults on any corruption/IO problem
            }
            return new AgentSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions), Encoding.UTF8);
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        private static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue))
                return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
