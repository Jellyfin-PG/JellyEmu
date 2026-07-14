using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly JellyEmuPico8Manager _pico8Manager;
        private readonly JellyEmuThreeJsManager _threeJsManager;
        private readonly IServiceProvider _serviceProvider;
        private string? _resolvedPluginName;

        private static readonly Guid RegistrationId = Guid.Parse("9bab105e-9af0-4e25-a87d-876713b60962");

        public JellyEmuInjectorService(
            ILogger<JellyEmuInjectorService> logger,
            JellyEmuEjsManager ejsManager,
            JellyEmuPico8Manager pico8Manager,
            JellyEmuThreeJsManager threeJsManager,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _ejsManager = ejsManager;
            _pico8Manager = pico8Manager;
            _threeJsManager = threeJsManager;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// Resolves the PluginInterface type from whichever file-transform plugin
        /// is loaded, or returns null if neither is present.
        /// </summary>
        private Type? ResolvePluginInterface()
        {
            var targetPlugins = new List<(string AssemblyFragment, string TypeName)>();

            bool useLoom = Plugin.Instance?.Configuration?.UseLoomInjector ?? false;
            if (useLoom)
            {
                targetPlugins.Add((".Loom", "Jellyfin.Plugin.Loom.LoomInterface"));
            }
            else
            {
                targetPlugins.Add((".FileTransformation", "Jellyfin.Plugin.FileTransformation.PluginInterface"));
            }

            foreach ((string fragment, string typeName) in targetPlugins)
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
                    
                    if (fragment == ".Loom")
                    {
                        InitializeLoomServiceProvider(assembly);
                        _resolvedPluginName = "Loom";
                    }
                    else
                    {
                        _resolvedPluginName = "FileTransformation";
                    }
                    
                    return type;
                }
            }

            return null;
        }

        private void InitializeLoomServiceProvider(Assembly loomAssembly)
        {
            try
            {
                Type? loomPluginType = loomAssembly.GetType("Jellyfin.Plugin.Loom.Plugin");
                if (loomPluginType == null) return;

                PropertyInfo? instanceProp = loomPluginType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object? instance = instanceProp?.GetValue(null);
                if (instance == null) return;

                PropertyInfo? serviceProviderProp = loomPluginType.GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Instance);
                if (serviceProviderProp != null && serviceProviderProp.GetValue(instance) == null)
                {
                    serviceProviderProp.SetValue(instance, _serviceProvider);
                    _logger.LogDebug("[JellyEmu] Safely initialized Loom plugin ServiceProvider via reflection before registration.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[JellyEmu] Failed to initialize Loom ServiceProvider via reflection.");
            }
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

            _threeJsManager.EnsureRuntimeAsync();

            _pico8Manager.EnsureRuntimeAsync();

            try
            {
                Type? pluginInterface = ResolvePluginInterface();
                if (pluginInterface == null)
                {
                    _logger.LogWarning("[JellyEmu] No file transform plugin found. UI mods will not be applied.");
                    return Task.CompletedTask;
                }

                string pluginName = _resolvedPluginName ?? "FileTransformation";
                _logger.LogInformation("[JellyEmu] Registering UI injection with {PluginName} plugin...", pluginName);

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
                _logger.LogInformation("[JellyEmu] Successfully registered with {PluginName} plugin.", pluginName);
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
                string pluginName = _resolvedPluginName ?? "FileTransformation";
                pluginInterface?.GetMethod("DeregisterTransformation")?.Invoke(null, new object[] { RegistrationId });
                _logger.LogInformation("[JellyEmu] Deregistered from {PluginName} plugin.", pluginName);
            }
            catch (Exception ex)
            {
                string pluginName = _resolvedPluginName ?? "FileTransformation";
                _logger.LogError(ex, "[JellyEmu] Failed to deregister from {PluginName} plugin.", pluginName);
            }

            return Task.CompletedTask;
        }
    }
}