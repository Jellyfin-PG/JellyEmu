using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public class JellyEmuInjectorService : IHostedService
    {
        private readonly ILogger<JellyEmuInjectorService> _logger;

        public JellyEmuInjectorService(ILogger<JellyEmuInjectorService> logger)
        {
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[JellyEmu] Registering UI injection with File Transformation...");

            try
            {
                var payloadDefinition = new
                {
                    id = Guid.NewGuid().ToString(),
                    fileNamePattern = "index.html",
                    callbackAssembly = GetType().Assembly.FullName,
                    callbackClass = typeof(JellyEmuUIInjector).FullName,
                    callbackMethod = nameof(JellyEmuUIInjector.InjectMods)
                };

                Assembly? fileTransformationAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation") ?? false);

                if (fileTransformationAssembly == null)
                {
                    _logger.LogWarning("[JellyEmu] File Transformation plugin not found. UI mods will not be applied.");
                    return Task.CompletedTask;
                }

                Type? pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterfaceType == null)
                {
                    _logger.LogWarning("[JellyEmu] Could not find PluginInterface type in File Transformation assembly.");
                    return Task.CompletedTask;
                }

                Assembly? newtonsoftAssembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");

                object? jObjectPayload = payloadDefinition;
                if (newtonsoftAssembly != null)
                {
                    Type? jtokenType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JToken");
                    MethodInfo? fromObjectMethod = jtokenType?.GetMethod("FromObject", new[] { typeof(object) });
                    if (fromObjectMethod != null)
                        jObjectPayload = fromObjectMethod.Invoke(null, new object[] { payloadDefinition });
                }

                pluginInterfaceType.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { jObjectPayload });
                _logger.LogInformation("[JellyEmu] Successfully registered with File Transformation plugin.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to register injection payload.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
