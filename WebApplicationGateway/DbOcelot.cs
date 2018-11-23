using CSRedis;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
using Ocelot.Errors;
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
using Ocelot.Responder;
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

        public bool Limitation { get; set; } = true;
        public int LimitationCacheExpire { get; set; } = 1800;
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
                FROM DbOcelotFoundationGlobalConfiguration 
                WHERE IsDefault = 1 AND IsAvailable = 1
            ";

            using (var sqlConnection = new SqlConnection(_configuration.DbConnectionString))
            {
                var dbOcelotFoundationGlobalConfiguration = await sqlConnection.QueryFirstOrDefaultAsync<DbOcelotFoundationGlobalConfiguration>(dbOcelotGlobalConfigurationSql);
                if (dbOcelotFoundationGlobalConfiguration != null)
                {
                    var fileGlobalConfiguration = new FileGlobalConfiguration();

                    fileGlobalConfiguration.RequestIdKey = dbOcelotFoundationGlobalConfiguration.RequestIdKey;

                    if (!string.IsNullOrEmpty(dbOcelotFoundationGlobalConfiguration.ServiceDiscoveryProvider))
                    {
                        fileGlobalConfiguration.ServiceDiscoveryProvider = JsonConvert.DeserializeObject<FileServiceDiscoveryProvider>(dbOcelotFoundationGlobalConfiguration.ServiceDiscoveryProvider);
                    }

                    if (!string.IsNullOrEmpty(dbOcelotFoundationGlobalConfiguration.RateLimitOptions))
                    {
                        fileGlobalConfiguration.RateLimitOptions = JsonConvert.DeserializeObject<FileRateLimitOptions>(dbOcelotFoundationGlobalConfiguration.RateLimitOptions);
                    }

                    if (!string.IsNullOrEmpty(dbOcelotFoundationGlobalConfiguration.QoSOptions))
                    {
                        fileGlobalConfiguration.QoSOptions = JsonConvert.DeserializeObject<FileQoSOptions>(dbOcelotFoundationGlobalConfiguration.QoSOptions);
                    }

                    fileGlobalConfiguration.BaseUrl = dbOcelotFoundationGlobalConfiguration.BaseUrl;

                    if (!string.IsNullOrEmpty(dbOcelotFoundationGlobalConfiguration.LoadBalancerOptions))
                    {
                        fileGlobalConfiguration.LoadBalancerOptions = JsonConvert.DeserializeObject<FileLoadBalancerOptions>(dbOcelotFoundationGlobalConfiguration.LoadBalancerOptions);
                    }

                    fileGlobalConfiguration.DownstreamScheme = dbOcelotFoundationGlobalConfiguration.DownstreamScheme;

                    if (!string.IsNullOrEmpty(dbOcelotFoundationGlobalConfiguration.HttpHandlerOptions))
                    {
                        fileGlobalConfiguration.HttpHandlerOptions = JsonConvert.DeserializeObject<FileHttpHandlerOptions>(dbOcelotFoundationGlobalConfiguration.HttpHandlerOptions);
                    }

                    fileConfiguration.GlobalConfiguration = fileGlobalConfiguration;

                    string dbOcelotReRouteSql = @"
                        SELECT 
                            B.* 
                        FROM DbOcelotFoundationGlobalConfigurationReRoute AS A 
                        INNER JOIN DbOcelotFoundationReRoute AS B 
                        ON A.FoundationReRouteId = B.Id 
                        WHERE A.FoundationGlobalConfigurationId = @Id AND IsAvailable = 1
                    ";

                    var dbOcelotFoundationReRoutes = (await sqlConnection.QueryAsync<DbOcelotFoundationReRoute>(dbOcelotReRouteSql, new { dbOcelotFoundationGlobalConfiguration.Id }))?.AsList();
                    if (dbOcelotFoundationReRoutes != null && dbOcelotFoundationReRoutes.Count > 0)
                    {
                        var fileReRoutes = new List<FileReRoute>();
                        foreach (var dbOcelotReRoute in dbOcelotFoundationReRoutes)
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


    public class DbOcelotFoundationGlobalConfiguration
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

    public class DbOcelotFoundationGlobalConfigurationReRoute
    {
        public int Id { get; set; }
        public int FoundationGlobalConfigurationId { get; set; }
        public int FoundationReRouteId { get; set; }
    }

    public class DbOcelotFoundationReRoute
    {
        public int Id { get; set; }

        public int FoundationReRouteClassifyId { get; set; }

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

    public class DbOcelotFoundationReRouteClassify
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

            builder.Services.AddSingleton<IOcelotCache<DbOcelotCache>, DbOcelotRedisCache<DbOcelotCache>>();
            builder.Services.AddSingleton<IDbOcelotAuthenticationProcessor, DbOcelotAuthenticationProcessor>();

            builder.Services.AddSingleton<IDbOcelotAuthenticationRepository, DbOcelotAuthenticationSqlServerRepository>();

            builder.Services.AddSingleton<IOcelotCache<DbOcelotLimitationDetailRule>, DbOcelotRedisCache<DbOcelotLimitationDetailRule>>();
            builder.Services.AddSingleton<IOcelotCache<DbOcelotLimitationCounter?>, DbOcelotRedisCache<DbOcelotLimitationCounter?>>(); // ?

            builder.Services.AddSingleton<IDbOcelotLimitationRepository, DbOcelotLimitationSqlServerRepository>();

            builder.Services.AddSingleton<IDbOcelotLimitationProcessor, DbOcelotLimitationProcessor>();

            builder.Services.AddSingleton<IErrorsToHttpStatusCodeMapper, DbOcelotErrorsToHttpStatusCodeMapper>();

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

            builder.Services.AddSingleton<IOcelotCache<DbOcelotCache>, DbOcelotRedisCache<DbOcelotCache>>();
            builder.Services.AddSingleton<IDbOcelotAuthenticationProcessor, DbOcelotAuthenticationProcessor>();

            builder.Services.AddSingleton<IDbOcelotAuthenticationRepository, DbOcelotAuthenticationMySqlRepository>();

            builder.Services.AddSingleton<IOcelotCache<DbOcelotLimitationDetailRule>, DbOcelotRedisCache<DbOcelotLimitationDetailRule>>();
            builder.Services.AddSingleton<IOcelotCache<DbOcelotLimitationCounter?>, DbOcelotRedisCache<DbOcelotLimitationCounter?>>(); // ?

            builder.Services.AddSingleton<IDbOcelotLimitationRepository, DbOcelotLimitationMySqlRepository>();

            builder.Services.AddSingleton<IErrorsToHttpStatusCodeMapper, DbOcelotErrorsToHttpStatusCodeMapper>();

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
            RedisHelper.Set(region + "-" + key, JsonConvert.SerializeObject(value), (int)ttl.TotalSeconds);
        }

        public void AddAndDelete(string key, T value, TimeSpan ttl, string region)
        {
            Add(key, value, ttl, region);
        }

        public void ClearRegion(string region)
        {
            RedisHelper.Del(RedisHelper.Keys(region + "-" + "*"));
        }

        public T Get(string key, string region)
        {
            var result = RedisHelper.Get(region + "-" + key);
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
            RedisHelper.Set("InternalConfiguration", JsonConvert.SerializeObject(internalConfiguration));
            return new OkResponse();
        }

        public Response<IInternalConfiguration> Get()
        {
            var configuration = RedisHelper.Get<InternalConfiguration>("InternalConfiguration");
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

    public class DbOcelotAuthenticationGroupFoundationReRoute
    {
        public int Id { get; set; }
        public int AuthenticationGroupId { get; set; }
        public int FoundationReRouteId { get; set; }
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
                    var clientId = "client_id";
                    var path = context.DownstreamReRoute.UpstreamPathTemplate.OriginalValue;
                    var clientClaim = context.HttpContext.User.Claims.FirstOrDefault(p => p.Type == "client_id");

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

        public static IOcelotPipelineBuilder UseDbOcelotLimitationMiddleware(this IOcelotPipelineBuilder builder)
        {
            return builder.UseMiddleware<DbOcelotLimitationMiddleware>();
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

            // 自定义认证中间件
            builder.UseDbOcelotAuthenticationMiddleware();

            // 自定义限流中间件
            builder.UseDbOcelotLimitationMiddleware();

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
        private readonly IOcelotCache<DbOcelotCache> _cache;

        public DbOcelotAuthenticationProcessor(IDbOcelotAuthenticationRepository authenticationRepository, DbOcelotConfiguration configuration, IOcelotCache<DbOcelotCache> cache)
        {
            _authenticationRepository = authenticationRepository;
            _configuration = configuration;
            _cache = cache;
        }

        public async Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            var region = "Authentication";

            var key = string.Empty;

            using (var algorithm = SHA1.Create())
            {
                var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes($"{region}-{clientId}-{upstreamPathTemplate}"));
                key = BitConverter.ToString(bytes).Replace("-", string.Empty);
            }

            var cache = _cache.Get(key, region);

            if (cache != null)
            {
                return cache.Result;
            }
            else
            {
                var result = await _authenticationRepository.CheckAuthenticationAsync(clientId, upstreamPathTemplate);

                _cache.Add(key, new DbOcelotCache() { Time = DateTime.Now, Result = result }, TimeSpan.FromMinutes(_configuration.AuthenticationCacheExpire), region);

                return result;
            }
        }
    }


    public interface IDbOcelotAuthenticationRepository
    {
        Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate);
    }

    public class DbOcelotAuthenticationSqlServerRepository : IDbOcelotAuthenticationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotAuthenticationSqlServerRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            using (var connection = new SqlConnection(_configuration.DbConnectionString))
            {
                string sqlStr = @"
                    SELECT 
                        COUNT(1) 
                    FROM  DbOcelotAuthenticationClient AS A 
                    INNER JOIN DbOcelotAuthenticationClientGroup AS B ON A.Id=B.AuthenticationClientId 
                    INNER JOIN DbOcelotAuthenticationGroup AS C ON B.AuthenticationGroupId = C.Id 
                    INNER JOIN DbOcelotAuthenticationGroupFoundationReRoute AS D ON C.Id = D.AuthenticationGroupId 
                    INNER JOIN DbOcelotFoundationReRoute AS E ON D.FoundationReRouteId = E.Id 
                    WHERE A.Enabled = 1 AND A.ClientId = @ClientId AND E.IsAvailable = 1 AND E.UpstreamPathTemplate = @UpstreamPathTemplate";

                var result = await connection.QueryFirstOrDefaultAsync<int>(sqlStr, new { ClientId = clientId, UpstreamPathTemplate = upstreamPathTemplate });

                return result > 0;
            }
        }
    }

    public class DbOcelotAuthenticationMySqlRepository : IDbOcelotAuthenticationRepository
    {
        public Task<bool> CheckAuthenticationAsync(string clientId, string upstreamPathTemplate)
        {
            throw new NotImplementedException();
        }
    }


    public class DbOcelotCache
    {
        public DateTime Time { get; set; }
        public bool Result { get; set; }
    }


    public class DbOcelotLimitationGroupAuthenticationClient
    {
        public int Id { get; set; }
        public int LimitationGroupId { get; set; }
        public int AuthenticationClientId { get; set; }
    }

    public class DbOcelotLimitationGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int IsAvailable { get; set; }
    }

    public class DbOcelotLimitationGroupRuleFoundationReRoute
    {
        public int Id { get; set; }
        public int LimitationGroupId { get; set; }
        public int LimitationRuleFoundationReRouteId { get; set; }
    }

    public class DbOcelotLimitationRuleFoundationReRoute
    {
        public int Id { get; set; }
        public int LimitationRuleId { get; set; }
        public int FoundationReRouteId { get; set; }
    }

    public class DbOcelotLimitationRule
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Period { get; set; }
        public int Number { get; set; }
        public int IsAvailable { get; set; }
    }

    public class DbOcelotLimitationApproving
    {
        public int Id { get; set; }
        public int FoundationReRouteId { get; set; }
        public int AuthenticationClientId { get; set; }
    }


    public class DbOcelotLimitationMiddleware : OcelotMiddleware
    {
        private readonly OcelotRequestDelegate _requestDelegate;
        private readonly DbOcelotConfiguration _configuration;
        private readonly IDbOcelotLimitationProcessor _limitationProcessor;

        public DbOcelotLimitationMiddleware(OcelotRequestDelegate requestDelegate,
            DbOcelotConfiguration configuration,
            IDbOcelotLimitationProcessor limitationProcessor,
            IOcelotLoggerFactory loggerFactory)
            : base(loggerFactory.CreateLogger<DbOcelotLimitationMiddleware>())
        {
            _requestDelegate = requestDelegate;
            _configuration = configuration;
            _limitationProcessor = limitationProcessor;
        }

        public async Task Invoke(DownstreamContext context)
        {
            var clientId = "client_id";
            if (!context.IsError)
            {
                if (!_configuration.Limitation)
                {
                    Logger.LogInformation($"未启用客户端限流中间件");
                    await _requestDelegate.Invoke(context);
                }
                else
                {
                    if (!context.DownstreamReRoute.IsAuthenticated)
                    {
                        if (context.HttpContext.Request.Headers.Keys.Contains("client_id"))
                        {
                            clientId = context.HttpContext.Request.Headers["client_id"].First();
                        }
                    }
                    else
                    {
                        var clientClaim = context.HttpContext.User.Claims.FirstOrDefault(p => p.Type == "client_id");
                        if (!string.IsNullOrEmpty(clientClaim?.Value))
                        {
                            clientId = clientClaim?.Value;
                        }
                    }

                    var upstreamPathTemplate = context.DownstreamReRoute.UpstreamPathTemplate.OriginalValue;

                    if (await _limitationProcessor.CheckLimitationAsync(clientId, upstreamPathTemplate))
                    {
                        await _requestDelegate.Invoke(context);
                    }
                    else
                    {
                        var error = new DbOcelotLimitationError($"请求路由 {context.HttpContext.Request.Path}触发限流策略");
                        Logger.LogWarning($"路由地址 {context.HttpContext.Request.Path} 触发限流策略. {error}");
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

    public interface IDbOcelotLimitationProcessor
    {
        Task<bool> CheckLimitationAsync(string clientId, string upstreamPathTemplate);
    }

    public class DbOcelotLimitationProcessor : IDbOcelotLimitationProcessor
    {
        private readonly DbOcelotConfiguration _configuration;
        private readonly IDbOcelotLimitationRepository _clientRateLimitRepository;
        private readonly IOcelotCache<DbOcelotLimitationCounter?> _cacheLimitationCounter;
        private readonly IOcelotCache<DbOcelotCache> _cache;
        private readonly IOcelotCache<DbOcelotLimitationDetailRule> _cacheLimitationDetailRule;

        private static readonly object _locker = new object();

        public DbOcelotLimitationProcessor(DbOcelotConfiguration configuration,
            IDbOcelotLimitationRepository limitationRepository,
            IOcelotCache<DbOcelotLimitationCounter?> cacheLimitationCounter,
            IOcelotCache<DbOcelotCache> cache,
            IOcelotCache<DbOcelotLimitationDetailRule> cacheLimitationDetailRule)
        {
            _configuration = configuration;
            _clientRateLimitRepository = limitationRepository;
            _cacheLimitationCounter = cacheLimitationCounter;
            _cache = cache;
            _cacheLimitationDetailRule = cacheLimitationDetailRule;
        }

        public async Task<bool> CheckLimitationAsync(string clientId, string upstreamPathTemplate)
        {
            var result = false;

            var limitationDetails = new List<DbOcelotLimitationDetail>();

            result = !await CheckLimitationRuleFoundationReRouteAsync(upstreamPathTemplate); // 路径是否有限流策略

            if (!result)
            {
                var limitResult = await CheckLimitationRuleAuthenticationClientAsync(clientId, upstreamPathTemplate); // 客户端于该路径下是否被限流
                result = !limitResult.limitation;
                limitationDetails = limitResult.limitationDetails;
            }

            if (!result)
            {
                result = await CheckLimitationApprovingAsync(clientId, upstreamPathTemplate); // 白名单
            }

            if (!result)
            {
                result = CheckLimitation(limitationDetails); // 限流并开始计数
            }

            return result;
        }

        private async Task<bool> CheckLimitationRuleFoundationReRouteAsync(string upstreamPathTemplate)
        {
            var region = "LimitationRuleFoundationReRoute";

            var key = $"{upstreamPathTemplate}";

            var cache = _cache.Get(key, region);

            if (cache != null)
            {
                return cache.Result;
            }
            else
            {
                var result = await _clientRateLimitRepository.CheckLimitationRuleFoundationReRouteAsync(upstreamPathTemplate);

                _cache.Add(key, new DbOcelotCache() { Time = DateTime.Now, Result = result }, TimeSpan.FromSeconds(_configuration.LimitationCacheExpire), region);

                return result;
            }
        }

        private async Task<(bool limitation, List<DbOcelotLimitationDetail> limitationDetails)> CheckLimitationRuleAuthenticationClientAsync(string clientId, string upstreamPathTemplate)
        {
            var region = "LimitationRuleAuthenticationClient";

            var key = $"{clientId}-{upstreamPathTemplate}";

            var cache = _cacheLimitationDetailRule.Get(key, region);

            if (cache != null)
            {
                return (cache.limitation, cache.limitationDetails);
            }
            else
            {
                var result = await _clientRateLimitRepository.CheckLimitationRuleAuthenticationClientAsync(clientId, upstreamPathTemplate);

                _cacheLimitationDetailRule.Add(key, new DbOcelotLimitationDetailRule() { limitation = result.limitation, limitationDetails = result.limitationDetails }, TimeSpan.FromSeconds(_configuration.LimitationCacheExpire), region);

                return result;
            }
        }

        private async Task<bool> CheckLimitationApprovingAsync(string clientId, string upstreamPathTemplate)
        {
            var region = "LimitationApproving";

            var key = $"{clientId}-{upstreamPathTemplate}";

            var cache = _cache.Get(key, region);

            if (cache != null)
            {
                return cache.Result;
            }
            else
            {
                var result = await _clientRateLimitRepository.CheckLimitationApprovingAsync(clientId, upstreamPathTemplate);

                _cache.Add(key, new DbOcelotCache() { Time = DateTime.Now, Result = result }, TimeSpan.FromSeconds(_configuration.LimitationCacheExpire), region);

                return result;
            }
        }

        private bool CheckLimitation(List<DbOcelotLimitationDetail> limitationDetails)
        {
            var result = true;

            if (limitationDetails != null && limitationDetails.Count > 0)
            {
                foreach (var limitationDetail in limitationDetails)
                {
                    var counter = new DbOcelotLimitationCounter(DateTime.UtcNow, 1);

                    var region = "Limitation";

                    var key = string.Empty;

                    using (var algorithm = SHA1.Create())
                    {
                        var bytes = algorithm.ComputeHash(Encoding.UTF8.GetBytes($"{limitationDetail.ClientId}-{limitationDetail.Period}-{limitationDetail.UpstreamPathTemplate}"));
                        key = BitConverter.ToString(bytes).Replace("-", string.Empty);
                    }

                    var second = 0;

                    var length = limitationDetail.Period.Length - 1;
                    var value = limitationDetail.Period.Substring(0, length);
                    var type = limitationDetail.Period.Substring(length, 1);

                    switch (type)
                    {
                        case "d":
                            second = Convert.ToInt32(double.Parse(value) * 24 * 3600);
                            break;
                        case "h":
                            second = Convert.ToInt32(double.Parse(value) * 3600);
                            break;
                        case "m":
                            second = Convert.ToInt32(double.Parse(value) * 60);
                            break;
                        case "s":
                            second = Convert.ToInt32(value);
                            break;
                        default:
                            throw new FormatException($"{limitationDetail.Period} can't be converted to TimeSpan, unknown type {type}");
                    }

                    lock (_locker)
                    {
                        var cacheCounter = _cacheLimitationCounter.Get(key, region);

                        if (cacheCounter.HasValue)
                        {
                            counter = new DbOcelotLimitationCounter(cacheCounter.Value.Time, cacheCounter.Value.Total + 1);
                        }
                        else
                        {
                            _cacheLimitationCounter.Add(key, counter, TimeSpan.FromSeconds(second), region);
                        }
                    }

                    if (counter.Total > limitationDetail.Number)
                    {
                        result = false;
                    }

                    if (counter.Total > 1 && counter.Total <= limitationDetail.Number)
                    {
                        var totalSeconds = (int)(counter.Time.AddSeconds(second) - DateTime.UtcNow).TotalSeconds;

                        _cacheLimitationCounter.Add(key, counter, TimeSpan.FromSeconds(totalSeconds), region);
                    }
                }
            }

            return result;
        }
    }

    public interface IDbOcelotLimitationRepository
    {
        Task<(bool limitation, List<DbOcelotLimitationDetail> limitationDetails)> CheckLimitationRuleAuthenticationClientAsync(string clientId, string upstreamPathTemplate);
        Task<bool> CheckLimitationApprovingAsync(string clientId, string upstreamPathTemplate);
        Task<bool> CheckLimitationRuleFoundationReRouteAsync(string upstreamPathTemplate);
    }

    public class DbOcelotLimitationSqlServerRepository : IDbOcelotLimitationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotLimitationSqlServerRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<(bool limitation, List<DbOcelotLimitationDetail> limitationDetails)> CheckLimitationRuleAuthenticationClientAsync(string clientId, string upstreamPathTemplate)
        {
            using (var connection = new SqlConnection(_configuration.DbConnectionString))
            {
                string sqlStr = @"
                    SELECT 
	                    DISTINCT A.UpstreamPathTemplate,
	                    C.Period,
	                    C.Number,
	                    G.ClientId 
                    FROM DbOcelotFoundationReRoute AS A
                    INNER JOIN DbOcelotLimitationRuleFoundationReRoute AS B ON A.Id = B.FoundationReRouteId
                    INNER JOIN DbOcelotLimitationRule AS C ON B.LimitationRuleId = C.Id 
                    INNER JOIN DbOcelotLimitationGroupRuleFoundationReRoute AS D ON B.Id = D.LimitationRuleFoundationReRouteId
                    INNER JOIN DbOcelotLimitationGroup AS E ON D.LimitationGroupId = E.Id
                    INNER JOIN DbOcelotLimitationGroupAuthenticationClient AS F ON F.LimitationGroupId = E.Id 
                    INNER JOIN DbOcelotAuthenticationClient AS G ON F.AuthenticationClientId = G.Id
                    WHERE A.IsAvailable = 1 
                    AND A.UpstreamPathTemplate = @UpstreamPathTemplate 
                    AND C.IsAvailable = 1 
                    AND E.IsAvailable = 1 
                    AND G.ClientId = @ClientId 
                    AND G.Enabled = 1
                ";

                var result = (await connection.QueryAsync<DbOcelotLimitationDetail>(sqlStr, new { ClientId = clientId, UpstreamPathTemplate = upstreamPathTemplate }))?.AsList();

                if (result != null && result.Count > 0)
                {
                    return (true, result);
                }
                else
                {
                    return (false, null);
                }
            }
        }

        public async Task<bool> CheckLimitationApprovingAsync(string clientId, string upstreamPathTemplate)
        {
            using (var connection = new SqlConnection(_configuration.DbConnectionString))
            {
                string sqlStr = @"
                    SELECT 
	                    COUNT(1) 
                    FROM DbOcelotFoundationReRoute AS A
                    INNER JOIN DbOcelotLimitationApproving AS B ON A.Id = B.FoundationReRouteId
                    INNER JOIN DbOcelotAuthenticationClient AS C ON B.AuthenticationClientId = C.Id 
                    WHERE A.IsAvailable = 1 AND C.ClientId = @ClientId AND A.UpstreamPathTemplate = @UpstreamPathTemplate AND C.Enabled = 1
                ";

                var result = await connection.QueryFirstOrDefaultAsync<int>(sqlStr, new { ClientId = clientId, UpstreamPathTemplate = upstreamPathTemplate });

                return result > 0;
            }
        }

        public async Task<bool> CheckLimitationRuleFoundationReRouteAsync(string upstreamPathTemplate)
        {
            using (var connection = new SqlConnection(_configuration.DbConnectionString))
            {
                string sqlStr = @"
                    SELECT 
	                    COUNT(1) 
                    FROM DbOcelotFoundationReRoute AS A 
                    INNER JOIN DbOcelotLimitationRuleFoundationReRoute AS B ON A.Id = B.FoundationReRouteId
                    INNER JOIN DbOcelotLimitationRule AS C ON B.LimitationRuleId = C.Id 
                    WHERE A.IsAvailable = 1 AND A.UpstreamPathTemplate = @UpstreamPathTemplate AND C.IsAvailable = 1
                ";

                var result = await connection.QueryFirstOrDefaultAsync<int>(sqlStr, new { UpstreamPathTemplate = upstreamPathTemplate });

                return result > 0;
            }
        }
    }

    public class DbOcelotLimitationMySqlRepository : IDbOcelotLimitationRepository
    {
        private readonly DbOcelotConfiguration _configuration;

        public DbOcelotLimitationMySqlRepository(DbOcelotConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<(bool limitation, List<DbOcelotLimitationDetail> limitationDetails)> CheckLimitationRuleAuthenticationClientAsync(string clientId, string upstreamPathTemplate)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CheckLimitationApprovingAsync(string clientId, string upstreamPathTemplate)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CheckLimitationRuleFoundationReRouteAsync(string upstreamPathTemplate)
        {
            throw new NotImplementedException();
        }
    }


    public class DbOcelotLimitationDetail
    {
        public string UpstreamPathTemplate { get; set; }
        public string Period { get; set; }
        public int Number { get; set; }
        public string ClientId { get; set; }
    }

    public struct DbOcelotLimitationCounter
    {
        [JsonConstructor]
        public DbOcelotLimitationCounter(DateTime time, long total)
        {
            Time = time;
            Total = total;
        }

        public DateTime Time { get; private set; }
        public long Total { get; private set; }
    }

    public class DbOcelotLimitationDetailRule
    {
        public bool limitation { get; set; }
        public List<DbOcelotLimitationDetail> limitationDetails { get; set; }
    }


    public class DbOcelotLimitationError : Error
    {
        public DbOcelotLimitationError(string message) : base(message, OcelotErrorCode.RateLimitOptionsError)
        {

        }
    }

    public class DbOcelotErrorsToHttpStatusCodeMapper : IErrorsToHttpStatusCodeMapper
    {
        public int Map(List<Error> errors)
        {
            if (errors.Any(e => e.Code == OcelotErrorCode.UnauthenticatedError))
            {
                return 401;
            }

            if (errors.Any(e => e.Code == OcelotErrorCode.UnauthorizedError
                || e.Code == OcelotErrorCode.ClaimValueNotAuthorisedError
                || e.Code == OcelotErrorCode.ScopeNotAuthorisedError
                || e.Code == OcelotErrorCode.UserDoesNotHaveClaimError
                || e.Code == OcelotErrorCode.CannotFindClaimError))
            {
                return 403;
            }

            if (errors.Any(e => e.Code == OcelotErrorCode.RequestTimedOutError))
            {
                return 503;
            }

            if (errors.Any(e => e.Code == OcelotErrorCode.UnableToFindDownstreamRouteError))
            {
                return 404;
            }

            if (errors.Any(e => e.Code == OcelotErrorCode.UnableToCompleteRequestError))
            {
                return 500;
            }

            if (errors.Any(e => e.Code == OcelotErrorCode.RateLimitOptionsError))
            {
                return 429;
            }

            return 404;
        }
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
