﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Couchbase;
using Couchbase.Configuration.Client;
using Tweek.Drivers.Blob;
using Tweek.Drivers.Blob.WebClient;
using Newtonsoft.Json;
using Couchbase.Core.Serialization;
using Newtonsoft.Json.Serialization;
using FSharpUtils.Newtonsoft;
using Tweek.Utils;
using Tweek.Drivers.CouchbaseDriver;
using Engine.Core.Rules;
using Engine.Drivers.Context;
using Tweek.JPad.Utils;
using Tweek.JPad;
using Tweek.ApiService.NetCore.Diagnostics;
using System.Reflection;
using static LanguageExt.Prelude;

namespace Tweek.ApiService.NetCore
{
    class TweekContractResolver : DefaultContractResolver
    {
        protected override JsonContract CreateContract(Type objectType)
        {
            var contract = base.CreateContract(objectType);

            if (typeof(JsonValue).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()))
            {
                contract.Converter = new JsonValueConverter();
            }

            return contract;
        }
    }

    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            if (env.IsDevelopment())
            {
                builder.AddApplicationInsightsSettings(developerMode: true);
            }

            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            
            services.AddApplicationInsightsTelemetry(Configuration);

            var contextBucketName = Configuration["Couchbase.BucketName"];
            var contextBucketPassword = Configuration["Couchbase.Password"];

            InitCouchbaseCluster(contextBucketName, contextBucketPassword);
            var contextDriver = new CouchBaseDriver(ClusterHelper.GetBucket, contextBucketName);
            var couchbaseDiagnosticsProvider = new BucketConnectionIsAlive(ClusterHelper.GetBucket, contextBucketName);

            var rulesDriver = GetRulesDriver();
            var rulesDiagnostics = new RulesDriverStatusService(rulesDriver);

            var parser = GetRulesParser();
            var tweek = Task.Run(async () => await Engine.Tweek.Create(contextDriver, rulesDriver, parser)).Result;

            services.AddSingleton(tweek);
            services.AddSingleton<IContextDriver>(contextDriver);
            services.AddSingleton(parser);
            services.AddSingleton<IEnumerable<IDiagnosticsProvider>>(new IDiagnosticsProvider[] {rulesDiagnostics, couchbaseDiagnosticsProvider});
            var tweekContactResolver = new TweekContractResolver();
            var jsonSerializer = new JsonSerializer() { ContractResolver = tweekContactResolver };
            services.AddSingleton(jsonSerializer);
            services.AddMvc().AddJsonOptions(opt =>
            {
                opt.SerializerSettings.ContractResolver = tweekContactResolver;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                loggerFactory.AddConsole(Configuration.GetSection("Logging"));
                loggerFactory.AddDebug();
            }
            Console.WriteLine(System.Runtime.GCSettings.IsServerGC);
            Console.WriteLine(System.Runtime.GCSettings.LatencyMode);
            app.UseApplicationInsightsRequestTelemetry();
            app.UseApplicationInsightsExceptionTelemetry();

            app.UseMvc();
        }

        private void InitCouchbaseCluster(string bucketName, string bucketPassword)
        {
            var url = Configuration["Couchbase.Url"];

            ClusterHelper.Initialize(new ClientConfiguration
            {
                Servers = new List<Uri> { new Uri(url) },
                BucketConfigs = new Dictionary<string, BucketConfiguration>
                {
                    [bucketName] = new BucketConfiguration
                    {
                        BucketName = bucketName,
                        Password = bucketPassword,
                        PoolConfiguration = new PoolConfiguration()
                        {
                            MaxSize = 30,
                            MinSize = 5
                        }
                    }
                },
                Serializer = () => new DefaultSerializer(
                   new JsonSerializerSettings()
                   {
                       ContractResolver = new TweekContractResolver()
                   },
                   new JsonSerializerSettings()
                   {
                       ContractResolver = new TweekContractResolver()
                   })
            });
        }

        private BlobRulesDriver GetRulesDriver()
        {
            return new BlobRulesDriver(new Uri(Configuration["RulesBlob.Url"]), new SystemWebClientFactory());
        }

        IRuleParser GetRulesParser()
        {

            return JPadRulesParserAdapter.Convert(new JPadParser(new ParserSettings(
                Comparers: Microsoft.FSharp.Core.FSharpOption<IDictionary<string, ComparerDelegate>>.Some(new Dictionary<string, ComparerDelegate>()
                {
                    ["version"] = Version.Parse
                }), Sha1Provider: Microsoft.FSharp.Core.FSharpOption<Sha1Provider>.Some((s)=>
                {
                    using (var sha1 = System.Security.Cryptography.SHA1.Create())
                    {
                        return sha1.ComputeHash(s);
                    }
                }))));
        }
    }
}