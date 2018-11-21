using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Administration;
using Ocelot.DependencyInjection;

namespace WebApplicationGateway
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication()
                .AddIdentityServerAuthentication("TestKey", options => {
                    options.Authority = "http://localhost:6611";
                    options.ApiName = "gateway";
                    options.RequireHttpsMetadata = false;
                });

            services.AddOcelot()
                    .AddDbOcelotSqlServer(options =>
                    {
                        options.DbConnectionString = @"Data Source=(localdb)\ProjectsV13;Initial Catalog=ocelot;Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=False;ApplicationIntent=ReadWrite;MultiSubnetFailover=False";
                        options.RedisConnectionStrings = new List<string>() {
                            "127.0.0.1:6379,password=1,defaultDatabase=0,poolsize=50,ssl=false,writeBuffer=10240,connectTimeout=1000,connectRetry=1;"
                        };
                    })
                    .AddAdministration("/ocelot", options =>
                    {
                        options.Authority = "http://localhost:6611"; //IdentityServer地址
                        options.RequireHttpsMetadata = false;
                        options.ApiName = "gateway_admin"; //网关管理的名称，对应的为客户端授权的scope
                    });
            // localhost:5000/ocelot/configuration GET/POST 获取/修改
            // localhost:5000/ocelot/outputcache/{region} DELETE region -> 数据库对应region
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseDbOcelot().Wait();
        }
    }
}

// Install-Package Dapper
// Install-Package Ocelot
// Install-Package Ocelot.Administration
// Install-Package CSRedisCore