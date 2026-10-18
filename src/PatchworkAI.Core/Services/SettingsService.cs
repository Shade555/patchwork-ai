using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Core.Services
{
    public interface ISettingsService
    {
        Task<UserSettings> LoadSettingsAsync();
        Task SaveSettingsAsync(UserSettings settings);
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public SettingsService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                _settingsFilePath = customPath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var dir = Path.Combine(appData, "PatchworkAI");
                _settingsFilePath = Path.Combine(dir, "settings.json");
            }
        }

        public async Task<UserSettings> LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                    if (settings != null) return settings;
                }
            }
            catch
            {
                // Fall back to defaults on error
            }

            return new UserSettings();
        }

        public async Task SaveSettingsAsync(UserSettings settings)
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(settings, JsonOptions);
                await File.WriteAllTextAsync(_settingsFilePath, json);
            }
            catch
            {
                // Silently ignore or rethrow if fatal
            }
        }
    }
}
