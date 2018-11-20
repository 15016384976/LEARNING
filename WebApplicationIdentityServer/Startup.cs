using System.Collections.Generic;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace WebApplicationIdentityServer
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryApiResources(new List<ApiResource>
            {
                new ApiResource("api1", "My API"),
                new ApiResource("gateway_admin", "My admin API")
            })
                .AddInMemoryClients(new List<Client>
            {
                new Client
                {
                    ClientId = "client1",
                    AllowedGrantTypes = GrantTypes.ClientCredentials,

                    ClientSecrets =
                    {
                        new Secret("secret1".Sha256())
                    },
                    AllowedScopes = { "api1" }
                },
                new Client
                {
                    ClientId = "client2",
                    AllowedGrantTypes = GrantTypes.ClientCredentials,

                    ClientSecrets =
                    {
                        new Secret("secret2".Sha256())
                    },
                    AllowedScopes = { "gateway_admin" }
                }
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseIdentityServer();
        }
    }
}

// Install-Package IdentityServer4