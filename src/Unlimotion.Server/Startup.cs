using AutoMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceStack;
using ServiceStack.Configuration;
using Unlimotion.Server.Hubs;

namespace Unlimotion.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddSingleton(Configuration);
            services.AddSingleton<IAppSettings, AppSettings>();
            services.AddSingleton<AppHost>();
            services.AddSingleton<IHaveVersions, DotNetCorePackageList>();
            services.AddSingleton<DotNetVersionHelper>();

            services.AddRavenDbServices();

            services.AddSignalR();

            var mapper = AppModelMapping.ConfigureMapping();
            services.AddSingleton<IMapper>(mapper);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<ChatHub>("/chathub");
            });

            var host = (AppHostBase)app.ApplicationServices.GetService(typeof(AppHost));

            new ServiceStackKey().Register(Configuration);

            app.UseServiceStack(host);
        }
    }
}