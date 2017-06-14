using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Net;

using Orleans.Runtime;
using Orleans.Runtime.Configuration;
using Orleans.Providers;
using StatsdClient;

namespace SBTech.OrleansStatsDUtils
{
    class State
    {
        public string DeploymentId { get; set; } = "";
        public bool IsSilo { get; set; } = true;        
        public string SiloOrClientId { get; set; } = "";
        public string Address { get; set; } = "";
        public string GatewayAddress { get; set; } = "";
        public string HostName { get; set; } = "";        

        public string StatsDServerName { get; set; } = "127.0.0.1";
        public int StatsDServerPort { get; set; } = 8125;
        public string StatsDPrefix { get; set; } = "";
        public int StatsDMaxUdpPacketSize { get; set; } = 512;
    }

    public class StatsDStatisticsProvider : IProvider,
                                            IConfigurableSiloMetricsDataPublisher,
                                            IConfigurableClientMetricsDataPublisher,
                                            IStatisticsPublisher
    {
        State _state = new State();

        public string Name { get; private set; }

        public Task Close() => Task.CompletedTask;

        public Task Init(string name, IProviderRuntime providerRuntime, IProviderConfiguration config)
        {
            Debug.WriteLine($"{nameof(StatsDStatisticsProvider)}.Init is invoked");

            Name = name;

            if (providerRuntime.ServiceProvider != null)
                _state.SiloOrClientId = providerRuntime.SiloIdentity;

            if (config.Properties.ContainsKey("StatsDServerName"))
                _state.StatsDServerName = config.Properties["StatsDServerName"];

            if (config.Properties.ContainsKey("StatsDServerPort"))
                _state.StatsDServerPort = int.Parse(config.Properties["StatsDServerPort"]);

            if (config.Properties.ContainsKey("StatsDPrefix"))
                _state.StatsDPrefix = config.Properties["StatsDPrefix"];

            if (config.Properties.ContainsKey("StatsDMaxUdpPacketSize"))
                _state.StatsDMaxUdpPacketSize = int.Parse(config.Properties["StatsDMaxUdpPacketSize"]);

            return Task.CompletedTask;
        }
        
        public Task Init(string deploymentId, string storageConnectionString, SiloAddress siloAddress,
                         string siloName, IPEndPoint gateway, string hostName)
        {
            _state.Address = siloAddress.Endpoint.ToString();
            _state.DeploymentId = deploymentId;            
            _state.GatewayAddress = gateway.ToString();
            _state.HostName = hostName;

            return Task.CompletedTask;
        }

        public Task Init(bool isSilo, string storageConnectionString, string deploymentId,
                         string address, string siloName, string hostName)
        {
            _state.DeploymentId = deploymentId;
            _state.IsSilo = isSilo;            
            _state.Address = address;
            _state.HostName = hostName;

            return Task.CompletedTask;
        }

        public Task Init(ClientConfiguration config, IPAddress address, string clientId)
        {
            _state.SiloOrClientId = clientId;
            _state.Address = address.ToString();

            return Task.CompletedTask;
        }

        public void AddConfiguration(string deploymentId, bool isSilo, string siloName, 
                                     SiloAddress address, IPEndPoint gateway, string hostName)
        {
            _state.DeploymentId = deploymentId;
            _state.IsSilo = isSilo;            
            _state.Address = address.ToString();
            _state.GatewayAddress = gateway.ToString();
            _state.HostName = hostName;

            InitStatsDClient(_state);
        }

        public void AddConfiguration(string deploymentId, string hostName, string clientId,
                                     IPAddress address)
        {
            _state.DeploymentId = deploymentId;
            _state.SiloOrClientId = clientId;
            _state.HostName = hostName;
            _state.Address = address.ToString();

            InitStatsDClient(_state);
        }

        public Task ReportMetrics(ISiloPerformanceMetrics metricsData)
        {
            Debug.WriteLine($"{nameof(StatsDStatisticsProvider)}.ReportMetrics for silo is invoked");

            try
            {
                SendCoreMetrics(metricsData);

                Metrics.GaugeAbsoluteValue("activations_count", metricsData.ActivationCount);
                Metrics.GaugeAbsoluteValue("recently_used_activations", metricsData.RecentlyUsedActivationCount);
                Metrics.GaugeAbsoluteValue("request_queue_length", metricsData.RequestQueueLength);
                Metrics.GaugeAbsoluteValue("is_overloaded", metricsData.IsOverloaded ? 1 : 0);
                Metrics.GaugeAbsoluteValue("client_count", metricsData.ClientCount);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{nameof(StatsDStatisticsProvider)}.ReportMetrics for silo is failed: {ex}");
            }
            return Task.CompletedTask;
        }

        public Task ReportMetrics(IClientPerformanceMetrics metricsData)
        {
            Debug.WriteLine($"{nameof(StatsDStatisticsProvider)}.ReportMetrics for client is invoked");

            try
            {
                SendCoreMetrics(metricsData);
                Metrics.GaugeAbsoluteValue("connected_gateway_count", metricsData.ConnectedGatewayCount);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"{nameof(StatsDStatisticsProvider)}.ReportMetrics for client is failed: {ex}");
            }
            return Task.CompletedTask;
        }

        public Task ReportStats(List<ICounter> statsCounters)
        {
            Debug.WriteLine($"{nameof(StatsDStatisticsProvider)}.ReportStats is invoked");

            try
            {
                var counters = statsCounters.Where(cs => cs.Storage == CounterStorage.LogAndTable);

                foreach (var counter in counters)
                {
                    var valueStr = counter.IsValueDelta
                                    ? counter.GetDeltaString()
                                    : counter.GetValueString();

                    if (float.TryParse(valueStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                    {
                        var counterName = counter.Name.ToLowerInvariant();

                        if (counter.IsValueDelta)
                            Metrics.GaugeDelta(counterName, value);
                        else
                            Metrics.GaugeAbsoluteValue(counterName, value);
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError($"ReportStats failed: {ex}");
            }
            return Task.CompletedTask;
        }

        void InitStatsDClient(State state)
        {
            var metricsConfig = new MetricsConfig
            {
                StatsdServerName = state.StatsDServerName,
                StatsdServerPort = state.StatsDServerPort,
                Prefix = string.IsNullOrEmpty(state.StatsDPrefix)
                            ? $"{state.HostName.ToLower()}.{state.SiloOrClientId.ToLower()}"
                            : $"{state.StatsDPrefix.ToLower()}.{state.HostName.ToLower()}",
                StatsdMaxUDPPacketSize = state.StatsDMaxUdpPacketSize
            };

            Debug.WriteLine($"{nameof(StatsDStatisticsProvider)}.InitStatsDClient is invoked: StatsdServerName:{state.StatsDServerName}, StatsdServerPort:{state.StatsDServerPort}");

            Metrics.Configure(metricsConfig);
        }

        void SendCoreMetrics(ICorePerformanceMetrics metricsData)
        {            
            Metrics.GaugeAbsoluteValue("cpu_usage", metricsData.CpuUsage);
            Metrics.GaugeAbsoluteValue("total_physical_memory", metricsData.TotalPhysicalMemory);
            Metrics.GaugeAbsoluteValue("available_physical_memory", metricsData.AvailablePhysicalMemory);
            Metrics.GaugeAbsoluteValue("memory_usage", metricsData.MemoryUsage);
            Metrics.GaugeAbsoluteValue("total_physical_memory", metricsData.TotalPhysicalMemory);
            Metrics.GaugeAbsoluteValue("send_queue_length", metricsData.SendQueueLength);
            Metrics.GaugeAbsoluteValue("receive_queue_length", metricsData.ReceiveQueueLength);
            Metrics.GaugeAbsoluteValue("sent_messages", metricsData.SentMessages);
            Metrics.GaugeAbsoluteValue("received_messages", metricsData.ReceivedMessages);
        }        
    }
}
