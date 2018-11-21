using CSRedis;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Ocelot.Authentication.Middleware;
using Ocelot.Authorisation.Middleware;
using Ocelot.Cache;
using Ocelot.Cache.Middleware;
using Ocelot.Configuration;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Repository;
using Ocelot.DependencyInjection;
using Ocelot.DownstreamRouteFinder.Middleware;
using Ocelot.DownstreamUrlCreator.Middleware;
using Ocelot.Errors.Middleware;
using Ocelot.Headers.Middleware;
using Ocelot.LoadBalancer.Middleware;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.Middleware.Pipeline;
using Ocelot.RateLimit.Middleware;
using Ocelot.Request.Middleware;
using Ocelot.Requester.Middleware;
using Ocelot.RequestId.Middleware;
using Ocelot.Responder.Middleware;
using Ocelot.Responses;
using Ocelot.WebSockets.Middleware;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace WebApplicationGateway
{
    public class DbOcelotConfiguration
    {
        public string DbConnectionString { get; set; }
        public List<string> RedisConnectionStrings { get; set; }

        public bool Authentication { get; set; } = true;
        public int AuthenticationCacheExpire { get; set; } = 1800;
        public string AuthenticationClientId { get; set; } = "client_id";
    }

    public class DbOcelotSqlServerFileConfigurationRepository : IFileConfigurationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotSqlServerFileConfigurationRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<Response<FileConfiguration>> Get()
        {
            var fileConfiguration = new FileConfiguration();

            var dbOcelotGlobalConfigurationSql = @"
                SELECT 
                    * 
                FROM DbOcelotGlobalConfiguration 
                WHERE IsDefault = 1 AND IsAvailable = 1
            ";

            using (var sqlConnection = new SqlConnection(_configuration.DbConnectionString))
            {
                var dbOcelotGlobalConfiguration = await sqlConnection.QueryFirstOrDefaultAsync<DbOcelotGlobalConfiguration>(dbOcelotGlobalConfigurationSql);
                if (dbOcelotGlobalConfiguration != null)
                {
                    var fileGlobalConfiguration = new FileGlobalConfiguration();

                    fileGlobalConfiguration.RequestIdKey = dbOcelotGlobalConfiguration.RequestIdKey;

                    if (!string.IsNullOrEmpty(dbOcelotGlobalConfiguration.ServiceDiscoveryProvider))
                    {
                        fileGlobalConfiguration.ServiceDiscoveryProvider = JsonConvert.DeserializeObject<FileServiceDiscoveryProvider>(dbOcelotGlobalConfiguration.ServiceDiscoveryProvider);
                    }

                    if (!string.IsNullOrEmpty(dbOcelotGlobalConfiguration.RateLimitOptions))
                    {
                        fileGlobalConfiguration.RateLimitOptions = JsonConvert.DeserializeObject<FileRateLimitOptions>(dbOcelotGlobalConfiguration.RateLimitOptions);
                    }

                    if (!string.IsNullOrEmpty(dbOcelotGlobalConfiguration.QoSOptions))
                    {
                        fileGlobalConfiguration.QoSOptions = JsonConvert.DeserializeObject<FileQoSOptions>(dbOcelotGlobalConfiguration.QoSOptions);
                    }

                    fileGlobalConfiguration.BaseUrl = dbOcelotGlobalConfiguration.BaseUrl;

                    if (!string.IsNullOrEmpty(dbOcelotGlobalConfiguration.LoadBalancerOptions))
                    {
                        fileGlobalConfiguration.LoadBalancerOptions = JsonConvert.DeserializeObject<FileLoadBalancerOptions>(dbOcelotGlobalConfiguration.LoadBalancerOptions);
                    }

                    fileGlobalConfiguration.DownstreamScheme = dbOcelotGlobalConfiguration.DownstreamScheme;

                    if (!string.IsNullOrEmpty(dbOcelotGlobalConfiguration.HttpHandlerOptions))
                    {
                        fileGlobalConfiguration.HttpHandlerOptions = JsonConvert.DeserializeObject<FileHttpHandlerOptions>(dbOcelotGlobalConfiguration.HttpHandlerOptions);
                    }

                    fileConfiguration.GlobalConfiguration = fileGlobalConfiguration;

                    string dbOcelotReRouteSql = @"
                        SELECT 
                            B.* 
                        FROM DbOcelotGlobalConfigurationReRoute AS A 
                        INNER JOIN DbOcelotReRoute AS B 
                        ON A.ReRouteId = B.Id 
                        WHERE A.GlobalConfigurationId = @Id AND IsAvailable = 1
                    ";

                    var dbOcelotReRoutes = (await sqlConnection.QueryAsync<DbOcelotReRoute>(dbOcelotReRouteSql, new { dbOcelotGlobalConfiguration.Id }))?.AsList();
                    if (dbOcelotReRoutes != null && dbOcelotReRoutes.Count > 0)
                    {
                        var fileReRoutes = new List<FileReRoute>();
                        foreach (var dbOcelotReRoute in dbOcelotReRoutes)
                        {
                            var fileReRoute = new FileReRoute();

                            // Timeout

                            fileReRoute.Priority = dbOcelotReRoute.Priority;

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.DelegatingHandlers))
                            {
                                fileReRoute.DelegatingHandlers = JsonConvert.DeserializeObject<List<string>>(dbOcelotReRoute.DelegatingHandlers);
                            }

                            fileReRoute.Key = dbOcelotReRoute.Key;

                            fileReRoute.UpstreamHost = dbOcelotReRoute.UpstreamHost;

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.DownstreamHostAndPorts))
                            {
                                fileReRoute.DownstreamHostAndPorts = JsonConvert.DeserializeObject<List<FileHostAndPort>>(dbOcelotReRoute.DownstreamHostAndPorts);
                            }

                            // HttpHandlerOptions

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.AuthenticationOptions))
                            {
                                fileReRoute.AuthenticationOptions = JsonConvert.DeserializeObject<FileAuthenticationOptions>(dbOcelotReRoute.AuthenticationOptions);
                            }

                            // RateLimitOptions

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.LoadBalancerOptions))
                            {
                                fileReRoute.LoadBalancerOptions = JsonConvert.DeserializeObject<FileLoadBalancerOptions>(dbOcelotReRoute.LoadBalancerOptions);
                            }

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.QoSOptions))
                            {
                                fileReRoute.QoSOptions = JsonConvert.DeserializeObject<FileQoSOptions>(dbOcelotReRoute.QoSOptions);
                            }

                            fileReRoute.DownstreamScheme = dbOcelotReRoute.DownstreamScheme;

                            fileReRoute.ServiceName = dbOcelotReRoute.ServiceName;

                            // ReRouteIsCaseSensitive

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.FileCacheOptions))
                            {
                                fileReRoute.FileCacheOptions = JsonConvert.DeserializeObject<FileCacheOptions>(dbOcelotReRoute.FileCacheOptions);
                            }

                            fileReRoute.RequestIdKey = dbOcelotReRoute.RequestIdKey;

                            // AddQueriesToRequest

                            // RouteClaimsRequirement

                            // AddClaimsToRequest 

                            // DownstreamHeaderTransform

                            // UpstreamHeaderTransform 

                            // AddHeadersToRequest 

                            if (!string.IsNullOrEmpty(dbOcelotReRoute.UpstreamHttpMethod))
                            {
                                fileReRoute.UpstreamHttpMethod = JsonConvert.DeserializeObject<List<string>>(dbOcelotReRoute.UpstreamHttpMethod);
                            }

                            fileReRoute.UpstreamPathTemplate = dbOcelotReRoute.UpstreamPathTemplate;

                            fileReRoute.DownstreamPathTemplate = dbOcelotReRoute.DownstreamPathTemplate;

                            // DangerousAcceptAnyServerCertificateValidator

                            // SecurityOptions

                            fileReRoutes.Add(fileReRoute);
                        }
                        fileConfiguration.ReRoutes = fileReRoutes;
                    }
                }
                else
                {
                    throw new Exception("未监测到任何可用的配置信息");
                }
            }

            if (fileConfiguration.ReRoutes == null || fileConfiguration.ReRoutes.Count == 0)
            {
                fileConfiguration = null;
            }

            return new OkResponse<FileConfiguration>(fileConfiguration);
        }

        public Task<Response> Set(FileConfiguration fileConfiguration)
        {
            throw new NotImplementedException();
        }
    }

    public class DbOcelotMySqlFileConfigurationRepository : IFileConfigurationRepository
    {
        public Task<Response<FileConfiguration>> Get()
        {
            throw new NotImplementedException();// MySqlConnection -> Install-Package MySql.Data.EntityFrameworkCore
        }

        public Task<Response> Set(FileConfiguration fileConfiguration)
        {
            throw new NotImplementedException();
        }
    }


    public class DbOcelotGlobalConfiguration
    {
        public int Id { get; set; }
        public string RequestIdKey { get; set; }
        public string ServiceDiscoveryProvider { get; set; }
        public string RateLimitOptions { get; set; }
        public string QoSOptions { get; set; }
        public string BaseUrl { get; set; }
        public string LoadBalancerOptions { get; set; }
        public string DownstreamScheme { get; set; }
        public string HttpHandlerOptions { get; set; }
        public int IsDefault { get; set; }
        public int IsAvailable { get; set; }
    }

    public class DbOcelotGlobalConfigurationReRoute
    {
        public int Id { get; set; }
        public int GlobalConfigurationId { get; set; }
        public int ReRouteId { get; set; }
    }

    public class DbOcelotReRoute
    {
        public int Id { get; set; }

        public int ReRouteClassifyId { get; set; }

        // public int Timeout { get; set; }

        public int Priority { get; set; }

        public string DelegatingHandlers { get; set; }

        public string Key { get; set; }

        public string UpstreamHost { get; set; }

        public string DownstreamHostAndPorts { get; set; }

        // public string HttpHandlerOptions { get; set; } 

        public string AuthenticationOptions { get; set; }

        // public string RateLimitOptions { get; set; }

        public string LoadBalancerOptions { get; set; }

        public string QoSOptions { get; set; }

        public string DownstreamScheme { get; set; }

        public string ServiceName { get; set; }

        // public bool ReRouteIsCaseSensitive { get; set; }

        public string FileCacheOptions { get; set; }

        public string RequestIdKey { get; set; }

        // public string AddQueriesToRequest { get; set; }

        // public string RouteClaimsRequirement { get; set; }

        // public string AddClaimsToRequest { get; set; }

        // public string DownstreamHeaderTransform { get; set; }

        // public string UpstreamHeaderTransform { get; set; }

        // public string AddHeadersToRequest { get; set; }

        public string UpstreamHttpMethod { get; set; }

        public string UpstreamPathTemplate { get; set; }

        public string DownstreamPathTemplate { get; set; }

        // public bool DangerousAcceptAnyServerCertificateValidator { get; set; }

        // public string SecurityOptions { get; set; }

        public int IsAvailable { get; set; }
    }

    public class DbOcelotReRouteClassify
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int ParentId { get; set; }
        public int IsAvailable { get; set; }
    }


    public static class DbOcelotBuilderExtension
    {
        public static IOcelotBuilder AddDbOcelotSqlServer(this IOcelotBuilder builder, Action<DbOcelotConfiguration> options)
        {
            builder.Services.Configure(options);
            builder.Services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<DbOcelotConfiguration>>().Value);

            builder.Services.AddSingleton<IFileConfigurationRepository, DbOcelotSqlServerFileConfigurationRepository>();

            builder.Services.AddSingleton<IOcelotCache<FileConfiguration>, DbOcelotRedisCache<FileConfiguration>>();
            builder.Services.AddSingleton<IOcelotCache<CachedResponse>, DbOcelotRedisCache<CachedResponse>>();

            builder.Services.AddSingleton<IInternalConfigurationRepository, DbOcelotRedisInternalConfigurationRepository>();

            builder.Services.AddSingleton<IOcelotCache<ClientRoleModel>, DbOcelotRedisCache<ClientRoleModel>>();
            builder.Services.AddSingleton<IDbOcelotAuthenticationProcessor, DbOcelotAuthenticationProcessor>();

            builder.Services.AddSingleton<IDbOcelotAuthenticationRepository, DbOcelotAuthenticationSqlServerRepository>();

            return builder;
        }

        public static IOcelotBuilder AddDbOcelotMySql(this IOcelotBuilder builder, Action<DbOcelotConfiguration> options)
        {
            builder.Services.Configure(options);
            builder.Services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<DbOcelotConfiguration>>().Value);

            builder.Services.AddSingleton<IFileConfigurationRepository, DbOcelotMySqlFileConfigurationRepository>();

            builder.Services.AddSingleton<IOcelotCache<FileConfiguration>, DbOcelotRedisCache<FileConfiguration>>();
            builder.Services.AddSingleton<IOcelotCache<CachedResponse>, DbOcelotRedisCache<CachedResponse>>();

            builder.Services.AddSingleton<IInternalConfigurationRepository, DbOcelotRedisInternalConfigurationRepository>();

            builder.Services.AddSingleton<IOcelotCache<ClientRoleModel>, DbOcelotRedisCache<ClientRoleModel>>();
            builder.Services.AddSingleton<IDbOcelotAuthenticationProcessor, DbOcelotAuthenticationProcessor>();

            builder.Services.AddSingleton<IDbOcelotAuthenticationRepository, DbOcelotAuthenticationMySqlRepository>();

            return builder;
        }
    }

    public static class DbOcelotApplicationBuilderExtension
    {
        public static async Task<IApplicationBuilder> UseDbOcelot(this IApplicationBuilder builder)
        {
            await builder.UseDbOcelot(new OcelotPipelineConfiguration());
            return builder;
        }

        public static async Task<IApplicationBuilder> UseDbOcelot(this IApplicationBuilder builder, Action<OcelotPipelineConfiguration> pipelineConfiguration)
        {
            var config = new OcelotPipelineConfiguration();
            pipelineConfiguration?.Invoke(config);
            return await builder.UseDbOcelot(config);
        }

        public static async Task<IApplicationBuilder> UseDbOcelot(this IApplicationBuilder builder, OcelotPipelineConfiguration pipelineConfiguration)
        {
            var configuration = await CreateConfiguration(builder);
            ConfigureDiagnosticListener(builder);
            return CreateOcelotPipeline(builder, pipelineConfiguration);
        }

        private static IApplicationBuilder CreateOcelotPipeline(IApplicationBuilder builder, OcelotPipelineConfiguration pipelineConfiguration)
        {
            var pipelineBuilder = new OcelotPipelineBuilder(builder.ApplicationServices);

            pipelineBuilder.BuildDbOcelotPipeline(pipelineConfiguration);// pipelineBuilder.BuildOcelotPipeline(pipelineConfiguration);

            var firstDelegate = pipelineBuilder.Build();
            builder.Properties["analysis.NextMiddlewareName"] = "TransitionToOcelotMiddleware";
            builder.Use(async (context, task) =>
            {
                var downstreamContext = new DownstreamContext(context);
                await firstDelegate.Invoke(downstreamContext);
            });
            return builder;
        }

        private static async Task<IInternalConfiguration> CreateConfiguration(IApplicationBuilder builder)
        {
            var fileConfig = await builder.ApplicationServices.GetService<IFileConfigurationRepository>().Get();
            var internalConfigCreator = builder.ApplicationServices.GetService<IInternalConfigurationCreator>();
            var internalConfig = await internalConfigCreator.Create(fileConfig.Data);
            if (internalConfig.IsError)
            {
                ThrowToStopOcelotStarting(internalConfig);
            }
            var internalConfigRepo = builder.ApplicationServices.GetService<IInternalConfigurationRepository>();
            internalConfigRepo.AddOrReplace(internalConfig.Data);
            var configurations = builder.ApplicationServices.GetServices<OcelotMiddlewareConfigurationDelegate>();
            foreach (var configuration in configurations)
            {
                await configuration(builder);
            }
            return GetOcelotConfigAndReturn(internalConfigRepo);
        }

        private static bool IsError(Response response)
        {
            return response == null || response.IsError;
        }

        private static IInternalConfiguration GetOcelotConfigAndReturn(IInternalConfigurationRepository provider)
        {
            var ocelotConfiguration = provider.Get();
            if (ocelotConfiguration?.Data == null || ocelotConfiguration.IsError)
            {
                ThrowToStopOcelotStarting(ocelotConfiguration);
            }
            return ocelotConfiguration.Data;
        }

        private static void ThrowToStopOcelotStarting(Response config)
        {
            throw new Exception($"Unable to start Ocelot, errors are: {string.Join(",", config.Errors.Select(x => x.ToString()))}");
        }

        private static void ConfigureDiagnosticListener(IApplicationBuilder builder)
        {
            var env = builder.ApplicationServices.GetService<IHostingEnvironment>();
            var listener = builder.ApplicationServices.GetService<OcelotDiagnosticListener>();
            var diagnosticListener = builder.ApplicationServices.GetService<DiagnosticListener>();
            diagnosticListener.SubscribeWithAdapter(listener);
        }
    }

    public class DbOcelotRedisCache<T> : IOcelotCache<T>
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotRedisCache(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;

            if (configuration.RedisConnectionStrings.Count == 1)
            {
                RedisHelper.Initialization(new CSRedisClient(configuration.RedisConnectionStrings[0]));
            }
            else
            {
                RedisHelper.Initialization(new CSRedisClient(null, configuration.RedisConnectionStrings.ToArray()));
            }
        }

        public void Add(string key, T value, TimeSpan ttl, string region)
        {
            if (ttl.TotalMilliseconds <= 0) return;
            RedisHelper.Set("test" + "-" + region + "-" + key, JsonConvert.SerializeObject(value), (int)ttl.TotalSeconds);
        }

        public void AddAndDelete(string key, T value, TimeSpan ttl, string region)
        {
            Add(key, value, ttl, region);
        }

        public void ClearRegion(string region)
        {
            RedisHelper.Del(RedisHelper.Keys("test" + "-" + region + "-" + "*"));
        }

        public T Get(string key, string region)
        {
            var result = RedisHelper.Get("test" + "-" + region + "-" + key);
            if (string.IsNullOrEmpty(result)) return default(T);
            return JsonConvert.DeserializeObject<T>(result);
        }
    }

    public class DbOcelotRedisInternalConfigurationRepository : IInternalConfigurationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotRedisInternalConfigurationRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;

            if (configuration.RedisConnectionStrings.Count == 1)
            {
                RedisHelper.Initialization(new CSRedisClient(configuration.RedisConnectionStrings[0]));
            }
            else
            {
                RedisHelper.Initialization(new CSRedisClient(null, configuration.RedisConnectionStrings.ToArray()));
            }
        }

        public Response AddOrReplace(IInternalConfiguration internalConfiguration)
        {
            RedisHelper.Set("test" + "-" + "internal" + "-" + "configuration", JsonConvert.SerializeObject(internalConfiguration));
            return new OkResponse();
        }

        public Response<IInternalConfiguration> Get()
        {
            var configuration = RedisHelper.Get<InternalConfiguration>("test" + "-" + "internal" + "-" + "configuration");
            return new OkResponse<IInternalConfiguration>(configuration ?? default(InternalConfiguration));
        }
    }


    public class DbOcelotAuthenticationClient
    {
        public int Id { get; set; }
        public int AbsoluteRefreshTokenLifetime { get; set; }
        public int AccessTokenLifetime { get; set; }
        public int AccessTokenType { get; set; }
        public bool AllowAccessTokensViaBrowser { get; set; }
        public bool AllowOfflineAccess { get; set; }
        public bool AllowPlainTextPkce { get; set; }
        public bool AllowRememberConsent { get; set; }
        public bool AlwaysIncludeUserClaimsInIdToken { get; set; }
        public bool AlwaysSendClientClaims { get; set; }
        public int AuthorizationCodeLifetime { get; set; }
        public bool BackChannelLogoutSessionRequired { get; set; }
        public string BackChannelLogoutUri { get; set; }
        public string ClientClaimsPrefix { get; set; }
        public string ClientId { get; set; }
        public string ClientName { get; set; }
        public string ClientUri { get; set; }
        public int? ConsentLifetime { get; set; }
        public string Description { get; set; }
        public bool EnableLocalLogin { get; set; }
        public bool Enabled { get; set; }
        public bool FrontChannelLogoutSessionRequired { get; set; }
        public string FrontChannelLogoutUri { get; set; }
        public int IdentityTokenLifetime { get; set; }
        public bool IncludeJwtId { get; set; }
        public string LogoUri { get; set; }
        public string PairWiseSubjectSalt { get; set; }
        public string ProtocolType { get; set; }
        public int RefreshTokenExpiration { get; set; }
        public int RefreshTokenUsage { get; set; }
        public bool RequireClientSecret { get; set; }
        public bool RequireConsent { get; set; }
        public bool RequirePkce { get; set; }
        public int SlidingRefreshTokenLifetime { get; set; }
        public bool UpdateAccessTokenClaimsOnRefresh { get; set; }
    }

    public class DbOcelotAuthenticationClientGroup
    {
        public int Id { get; set; }
        public int AuthenticationClientId { get; set; }
        public int AuthenticationGroupId { get; set; }
    }

    public class DbOcelotAuthenticationGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int IsAvailable { get; set; }
    }

    public class DbOcelotAuthenticationGroupReRoute
    {
        public int Id { get; set; }
        public int AuthenticationGroupId { get; set; }
        public int ReRouteId { get; set; }
    }


    public class DbOcelotAuthenticationMiddleware : OcelotMiddleware
    {
        private readonly OcelotRequestDelegate _requestDelegate;
        private readonly DbOcelotConfiguration _configuration;
        private readonly IDbOcelotAuthenticationProcessor _authenticationProcessor;

        public DbOcelotAuthenticationMiddleware(OcelotRequestDelegate requestDelegate,
            DbOcelotConfiguration configuration,
            IDbOcelotAuthenticationProcessor authenticationProcessor,
            IOcelotLoggerFactory loggerFactory)
            : base(loggerFactory.CreateLogger<DbOcelotAuthenticationMiddleware>())
        {
            _requestDelegate = requestDelegate;
            _configuration = configuration;
            _authenticationProcessor = authenticationProcessor;
        }

        public async Task Invoke(DownstreamContext context)
        {
            if (!context.IsError && context.HttpContext.Request.Method.ToUpper() != "OPTIONS" && context.DownstreamReRoute.IsAuthenticated)
            {
                if (!_configuration.Authentication)
                {
                    Logger.LogInformation($"未启用客户端授权认证管道");
                    await _requestDelegate.Invoke(context);
                }
                else
                {
                    Logger.LogInformation($"{context.HttpContext.Request.Path} 是认证路由. {MiddlewareName} 开始校验授权信息");
                    var clientId = "client_cjy";
                    var path = context.DownstreamReRoute.UpstreamPathTemplate.OriginalValue;
                    var clientClaim = context.HttpContext.User.Claims.FirstOrDefault(p => p.Type == _configuration.AuthenticationClientId);

                    if (!string.IsNullOrEmpty(clientClaim?.Value))
                    {
                        clientId = clientClaim?.Value;
                    }

                    if (await _authenticationProcessor.CheckAuthenticationAsync(clientId, path))
                    {
                        await _requestDelegate.Invoke(context);
                    }
                    else
                    {
                        var error = new UnauthenticatedError($"请求认证路由 {context.HttpContext.Request.Path}客户端未授权");
                        Logger.LogWarning($"路由地址 {context.HttpContext.Request.Path} 自定义认证管道校验失败. {error}");
                        SetPipelineError(context, error);
                    }
                }
            }
            else
            {
                await _requestDelegate.Invoke(context);
            }
        }
    }


    public static class DbOcelotPipelineBuilderExtension
    {
        public static IOcelotPipelineBuilder UseDbOcelotAuthenticationMiddleware(this IOcelotPipelineBuilder builder)
        {
            return builder.UseMiddleware<DbOcelotAuthenticationMiddleware>();
        }

        public static OcelotRequestDelegate BuildDbOcelotPipeline(this IOcelotPipelineBuilder builder, OcelotPipelineConfiguration pipelineConfiguration)
        {
            builder.UseExceptionHandlerMiddleware();
            builder.MapWhen(context => context.HttpContext.WebSockets.IsWebSocketRequest,
                app =>
                {
                    app.UseDownstreamRouteFinderMiddleware();
                    app.UseDownstreamRequestInitialiser();
                    app.UseLoadBalancingMiddleware();
                    app.UseDownstreamUrlCreatorMiddleware();
                    app.UseWebSocketsProxyMiddleware();
                });
            builder.UseIfNotNull(pipelineConfiguration.PreErrorResponderMiddleware);
            builder.UseResponderMiddleware();
            builder.UseDownstreamRouteFinderMiddleware();
            if (pipelineConfiguration.MapWhenOcelotPipeline != null)
            {
                foreach (var pipeline in pipelineConfiguration.MapWhenOcelotPipeline)
                {
                    builder.MapWhen(pipeline);
                }
            }
            builder.UseHttpHeadersTransformationMiddleware();
            builder.UseDownstreamRequestInitialiser();
            builder.UseRateLimiting();
            builder.UseRequestIdMiddleware();
            builder.UseIfNotNull(pipelineConfiguration.PreAuthenticationMiddleware);
            if (pipelineConfiguration.AuthenticationMiddleware == null)
            {
                builder.UseAuthenticationMiddleware();
            }
            else
            {
                builder.Use(pipelineConfiguration.AuthenticationMiddleware);
            }

            builder.UseDbOcelotAuthenticationMiddleware(); //

            builder.UseIfNotNull(pipelineConfiguration.PreAuthorisationMiddleware);
            if (pipelineConfiguration.AuthorisationMiddleware == null)
            {
                builder.UseAuthorisationMiddleware();
            }
            else
            {
                builder.Use(pipelineConfiguration.AuthorisationMiddleware);
            }
            builder.UseIfNotNull(pipelineConfiguration.PreQueryStringBuilderMiddleware);
            builder.UseLoadBalancingMiddleware();
            builder.UseDownstreamUrlCreatorMiddleware();
            builder.UseOutputCacheMiddleware();
            builder.UseHttpRequesterMiddleware();
            return builder.Build();
        }

        private static void UseIfNotNull(this IOcelotPipelineBuilder builder, Func<DownstreamContext, Func<Task>, Task> middleware)
        {
            if (middleware != null) builder.Use(middleware);
        }
    }


    public interface IDbOcelotAuthenticationProcessor
    {
        Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate);
    }

    public class DbOcelotAuthenticationProcessor : IDbOcelotAuthenticationProcessor
    {
        private readonly IDbOcelotAuthenticationRepository _authenticationRepository;
        private readonly DbOcelotConfiguration _configuration;
        private readonly IOcelotCache<ClientRoleModel> _cache;

        public DbOcelotAuthenticationProcessor(IDbOcelotAuthenticationRepository authenticationRepository, DbOcelotConfiguration configuration, IOcelotCache<ClientRoleModel> cache)
        {
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            var enablePrefix = "test" + "Authentication";
            var key = AhphOcelotHelper.ComputeCounterKey(enablePrefix, clientId, "", upstreamPathTemplate);
            var cacheResult = _cache.Get(key, enablePrefix);
            if (cacheResult != null)
            {
                return cacheResult.Role;
            }
            else
            {
                var result = await _authenticationRepository.AuthenticationAsync(clientId, upstreamPathTemplate);
                _cache.Add(key, new ClientRoleModel() { CacheTime = DateTime.Now, Role = result }, TimeSpan.FromMinutes(_configuration.AuthenticationCacheExpire), enablePrefix);
                return result;
            }
        }
    }


    public interface IDbOcelotAuthenticationRepository
    {
        Task<bool> AuthenticationAsync(string clientId, string upstreamPathTemplate);
    }

    public class DbOcelotAuthenticationSqlServerRepository : IDbOcelotAuthenticationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotAuthenticationSqlServerRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<bool> AuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            using (var connection = new SqlConnection(_configuration.DbConnectionString))
            {
                string sqlStr = @"
                    SELECT 
                        COUNT(1) 
                    FROM  DbOcelotAuthenticationClient AS A 
                    INNER JOIN DbOcelotAuthenticationClientGroup AS B ON A.Id=B.AuthenticationClientId 
                    INNER JOIN DbOcelotAuthenticationGroup AS C ON B.AuthenticationGroupId = C.Id 
                    INNER JOIN DbOcelotAuthenticationGroupReRoute AS D ON C.Id = D.AuthenticationGroupId 
                    INNER JOIN DbOcelotReRoute AS E ON D.ReRouteId = E.Id 
                    WHERE A.Enabled = 1 AND A.ClientId = @ClientId AND E.IsAvailable = 1 AND E.UpstreamPathTemplate = @UpstreamPathTemplate";

                var result = await connection.QueryFirstOrDefaultAsync<int>(sqlStr, new { ClientId = clientId, UpstreamPathTemplate = upstreamPathTemplate });

                return result > 0;
            }
        }
    }

    public class DbOcelotAuthenticationMySqlRepository : IDbOcelotAuthenticationRepository
    {
        public Task<bool> AuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            throw new NotImplementedException();
        }
    }


    public static class AhphOcelotHelper
    {
        /// <summary>
        /// 获取加密后缓存的KEY
        /// </summary>
        /// <param name="CounterPrefix">缓存前缀，防止重复</param>
        /// <param name="ClientId">客户端ID</param>
        /// <param name="Period">限流策略，如 1s 2m 3h 4d</param>
        /// <param name="Path">限流地址,可使用通配符</param>
        /// <returns></returns>
        public static string ComputeCounterKey(string CounterPrefix, string ClientId, string Period, string Path)
        {
            var key = $"{CounterPrefix}_{ClientId}_{Period}_{Path}";
            var idBytes = Encoding.UTF8.GetBytes(key);
            byte[] hashBytes;
            using (var algorithm = SHA1.Create())
            {
                hashBytes = algorithm.ComputeHash(idBytes);
            }
            return BitConverter.ToString(hashBytes).Replace("-", string.Empty);
        }
    }

    public class ClientRoleModel
    {
        /// <summary>
        /// 缓存时间
        /// </summary>
        public DateTime CacheTime { get; set; }
        /// <summary>
        /// 是否有访问权限
        /// </summary>
        public bool Role { get; set; }
    }

    public class ErrorResult
    {
        /// <summary>
        /// 错误代码
        /// </summary>
        public int errcode { get; set; }

        /// <summary>
        /// 错误描述
        /// </summary>
        public string errmsg { get; set; }
    }
}

/*
INSERT INTO DbOcelotAuthenticationClient(ClientId,ClientName,Enabled) VALUES('client1','测试客户端1',1)
INSERT INTO DbOcelotAuthenticationClient(ClientId,ClientName,Enabled) VALUES('client2','测试客户端2',1)

INSERT INTO DbOcelotAuthenticationGroup VALUES('授权组1','只能访问/cjy/values路由',1);
INSERT INTO DbOcelotAuthenticationGroup VALUES('授权组2','能访问所有路由',1);

INSERT INTO DbOcelotAuthenticationGroupReRoute VALUES(1,1);
INSERT INTO DbOcelotAuthenticationGroupReRoute VALUES(2,1);
INSERT INTO DbOcelotAuthenticationGroupReRoute VALUES(2,2);

INSERT INTO DbOcelotAuthenticationClientGroup VALUES(1,1);
INSERT INTO DbOcelotAuthenticationClientGroup VALUES(2,2);
*/
