﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NStatsD
{
    public class StatBucket
    {
        Stopwatch _timer;
        public string Name { get; private set; }
        private readonly StatsDConfigurationSection _config;

        public StatBucket(StatsDConfigurationSection config, string name)
        {
            Name = name;
            _config = config;
        }

        public StatBucket BeginTimer()
        {
            _timer = Stopwatch.StartNew();
            return this;
        }

        public void EndTimer(double sampleRate = 1)
        {
            _timer.Stop();
            Timing(_timer.ElapsedMilliseconds, sampleRate);
        }

        public void Timing(TimeSpan timeSpan, double sampleRate = 1)
        {
            Timing((long) timeSpan.TotalMilliseconds, sampleRate);
        }

        public void Timing(long time, double sampleRate = 1)
        {
            var data = new Dictionary<string, string> { { Name, string.Format("{0}|ms", time) } };
            Send(data, sampleRate);
        }

        public void Increment(double sampleRate = 1)
        {
            UpdateStats(1, sampleRate);
        }

        public void Decrement(double sampleRate = 1)
        {
            UpdateStats(-1, sampleRate);
        }

        public void Gauge(int value, double sampleRate = 1)
        {
            var data = new Dictionary<string, string> { { Name, string.Format("{0}|g", value) } };
            Send(data, sampleRate);
        }

        public void UpdateStats(int delta = 1, double sampleRate = 1)
        {
            var dictionary = new Dictionary<string, string> { { Name, string.Format("{0}|c", delta) } };
            Send(dictionary, sampleRate);
        }

        private void Send(Dictionary<string, string> data, double sampleRate = 1)
        {
            if (_config == null)
            {
                // silently exit if the user hasn't setup the stats due to unit test environment etc
                return;
            }
            
            var sampledData = new Dictionary<string, string>();
            if (sampleRate < 1)
            {
                var random = new Random();
                if (random.Next(0, 1) <= sampleRate)
                {
                    foreach (var stat in data.Keys)
                    {
                        sampledData.Add(stat, string.Format("{0}|@{1}", data[stat], sampleRate));
                    }       
                }
            }
            
            if (sampledData.Keys.Count == 0)
            {
                sampledData = data;
            }

            Task task = new Task(() => BackgroundSend(sampledData), TaskCreationOptions.LongRunning);
            task.Start();

            Log("NStatsD.Send returning");
        }

        /// <summary>
        /// Disposing of the UdpClient when the statsD server is down takes 3 seconds.
        /// So ship this off onto a background task.
        /// </summary>
        private void BackgroundSend(Dictionary<string, string> sampledData)
        {
            var host = _config.Server.Host;
            var port = _config.Server.Port;
            using (var client = new UdpClient(host, port))
            {
                foreach (var stat in sampledData.Keys)
                {
                    var encoding = new System.Text.ASCIIEncoding();
                    var stringToSend = string.Format("{0}:{1}", stat, sampledData[stat]);
                    var sendData = encoding.GetBytes(stringToSend);
                    Log("NStatsD sending {0}", stringToSend);
                    client.BeginSend(sendData, sendData.Length, Callback, null);
                    Log("NStatsD sent {0}", stringToSend);
                }
            }
        }

        private static void Log(string messageFormat, params object[] formatArgs)
        {
#if DEBUG
            Trace.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff\t") + string.Format(messageFormat, formatArgs));
#endif
        }

        private static void Callback(IAsyncResult result)
        {
            UdpClient udpClient = (UdpClient)result.AsyncState;
            Log("NStatsD sent: {0} bytes", udpClient == null ? "??" : udpClient.EndSend(result).ToString());
        }
    }
}