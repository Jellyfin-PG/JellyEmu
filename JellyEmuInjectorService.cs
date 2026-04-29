using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JellyEmu.Services
{
    public class JellyEmuInjectorService : IHostedService
    {
        private readonly ILogger<JellyEmuInjectorService> _logger;
        private readonly JellyEmuEjsManager _ejsManager;

        private static readonly Guid RegistrationId = Guid.Parse("9bab105e-9af0-4e25-a87d-876713b60962");

        private static readonly (string AssemblyFragment, string TypeName)[] KnownPlugins =
        {
            (".FileTransform",      "Jellyfin.Plugin.FileTransform.PluginInterface"),
            (".FileTransformation", "Jellyfin.Plugin.FileTransformation.PluginInterface"),
        };

        public JellyEmuInjectorService(
            ILogger<JellyEmuInjectorService> logger,
            JellyEmuEjsManager ejsManager)
        {
            _logger = logger;
            _ejsManager = ejsManager;
        }

        /// <summary>
        /// Resolves the PluginInterface type from whichever file-transform plugin
        /// is loaded, or returns null if neither is present.
        /// </summary>
        private Type? ResolvePluginInterface()
        {
            foreach ((string fragment, string typeName) in KnownPlugins)
            {
                Assembly? assembly = AssemblyLoadContext.All
                    .SelectMany(x => x.Assemblies)
                    .FirstOrDefault(x => x.FullName?.Contains(fragment) ?? false);

                if (assembly == null)
                    continue;

                Type? type = assembly.GetType(typeName);
                if (type != null)
                {
                    _logger.LogDebug("[JellyEmu] Resolved file transform plugin via '{Fragment}' → {TypeName}", fragment, typeName);
                    return type;
                }
            }

            return null;
        }

        private object? BuildJObjectPayload(object payloadDefinition)
        {
            Assembly? newtonsoftAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.GetName().Name == "Newtonsoft.Json");

            if (newtonsoftAssembly == null)
                return payloadDefinition;

            Type? jtokenType = newtonsoftAssembly.GetType("Newtonsoft.Json.Linq.JToken");
            MethodInfo? fromObjectMethod = jtokenType?.GetMethod("FromObject", new[] { typeof(object) });
            return fromObjectMethod != null
                ? fromObjectMethod.Invoke(null, new object[] { payloadDefinition })
                : payloadDefinition;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ejsManager.EnsureAssetsAsync();

            _logger.LogInformation("[JellyEmu] Registering UI injection with file transform plugin...");

            try
            {
                Type? pluginInterface = ResolvePluginInterface();
                if (pluginInterface == null)
                {
                    _logger.LogWarning("[JellyEmu] No file transform plugin found. UI mods will not be applied.");
                    return Task.CompletedTask;
                }

                var payloadDefinition = new
                {
                    id = RegistrationId.ToString(),
                    fileNamePattern = "index.html",
                    callbackAssembly = GetType().Assembly.FullName,
                    callbackClass = typeof(JellyEmuUIInjector).FullName,
                    callbackMethod = nameof(JellyEmuUIInjector.InjectMods)
                };

                object? jObjectPayload = BuildJObjectPayload(payloadDefinition);

                pluginInterface.GetMethod("RegisterTransformation")?.Invoke(null, new object?[] { jObjectPayload });
                _logger.LogInformation("[JellyEmu] Successfully registered with file transform plugin.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to register injection payload.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                Type? pluginInterface = ResolvePluginInterface();
                pluginInterface?.GetMethod("DeregisterTransformation")?.Invoke(null, new object[] { RegistrationId });
                _logger.LogInformation("[JellyEmu] Deregistered from file transform plugin.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JellyEmu] Failed to deregister from file transform plugin.");
            }

            return Task.CompletedTask;
        }
    }
}