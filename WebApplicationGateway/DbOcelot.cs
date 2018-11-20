using CSRedis;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Ocelot.Cache;
using Ocelot.Configuration;
using Ocelot.Configuration.Creator;
using Ocelot.Configuration.File;
using Ocelot.Configuration.Repository;
using Ocelot.DependencyInjection;
using Ocelot.Logging;
using Ocelot.Middleware;
using Ocelot.Middleware.Pipeline;
using Ocelot.Responses;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace WebApplicationGateway
{
    public class DbOcelotConfiguration
    {
        public string DbConnectionString { get; set; }
        public List<string> RedisConnectionStrings { get; set; }
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
                WHERE IsDefault=1 AND IsAvailable=1
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
                        FROM DbOcelotReRouteConfiguration AS A 
                        INNER JOIN DbOcelotReRoute AS B 
                        ON A.ReRouteId=B.Id 
                        WHERE A.GlobalConfigurationId=@Id AND IsAvailable=1
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

    public class DbOcelotReRoute
    {
        public int Id { get; set; }

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

    }

    public class DbOcelotReRouteConfiguration
    {

    }


    public static class DbOcelotServiceCollectionExtension
    {
        public static IOcelotBuilder AddDbOcelotSqlServer(this IOcelotBuilder builder, Action<DbOcelotConfiguration> options)
        {
            builder.Services.Configure(options);
            builder.Services.AddSingleton(resolver => resolver.GetRequiredService<IOptions<DbOcelotConfiguration>>().Value);

            builder.Services.AddSingleton<IFileConfigurationRepository, DbOcelotSqlServerFileConfigurationRepository>();

            builder.Services.AddSingleton<IOcelotCache<FileConfiguration>, DbOcelotRedisCache<FileConfiguration>>();
            builder.Services.AddSingleton<IOcelotCache<CachedResponse>, DbOcelotRedisCache<CachedResponse>>();

            builder.Services.AddSingleton<IInternalConfigurationRepository, DbOcelotRedisInternalConfigurationRepository>();

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

            return builder;
        }
    }

    public static class DbOcelotMiddlewareExtension
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
            pipelineBuilder.BuildOcelotPipeline(pipelineConfiguration);
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
}
