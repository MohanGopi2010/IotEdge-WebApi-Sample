using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.EventLog;
using Serilog;

namespace IotEdge_WebApi
{

    public class Program
    {
        static int counter;
        static ModuleClient _moduleClient = null;
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting Init.");
            Init().Wait();
            Console.WriteLine("Compeleted Init.");

            var datetime = DateTime.Now.ToString();
            Console.WriteLine($"***********{DateTime.Now}***********");
            Console.WriteLine("Starting HostBuilder");

            var host = CreateHostBuilder(args).Build();
            Console.WriteLine("Completed HostBuilder.");

            Console.WriteLine("Started Host.Run().");
            host.Run();
            Console.WriteLine("Completed Host.Run().");

            Console.WriteLine("Passed host run");

            //// Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        public static IWebHostBuilder CreateHostBuilder(string[] args)
        {


            Console.WriteLine("Entered CreateHostBuilder.");

            try
            {

                return WebHost.CreateDefaultBuilder(args)
                   .UseUrls($"http://+:10000")
                   .UseKestrel()
                   .UseSerilog((ctx, config) => { config.ReadFrom.Configuration(ctx.Configuration); })
                   .UseStartup<Startup>()
                   ;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to complete CreateHostBuilder.");
                Console.WriteLine(ex.Message);
                throw ex;
            }

        }



        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            try

            {
                // MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
                AmqpTransportSettings amqpSetting = new AmqpTransportSettings(Microsoft.Azure.Devices.Client.TransportType.Amqp_Tcp_Only);
                ITransportSettings[] settings = { amqpSetting };
                // return new ModuleClientAdapter(settings);

                // Open a connection to the Edge runtime
                _moduleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
                await _moduleClient.OpenAsync();
                Console.WriteLine("IoT Hub module client initialized.");

                await SendingMessage();

                Console.WriteLine("Before calling pipemessage");
                // Register callback to be called when a message is received by the module
                await _moduleClient.SetInputMessageHandlerAsync("input1", PipeMessage, _moduleClient);
                Console.WriteLine("After calling pipemessage");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

        static async Task SendingMessage()
        {

            var manifestMsg = new Message(Encoding.UTF8.GetBytes("Sample web api message test."));

            manifestMsg.Properties.Add("message-class", "informational");
            manifestMsg.Properties.Add("sent-timeUTC", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));

            await _moduleClient.SendEventAsync("output1", manifestMsg);
            Console.WriteLine("Sucessfully sent sample message to iot hub.");

        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            var counterValue = Interlocked.Increment(ref counter);
            try
            {
                ModuleClient moduleClient = (ModuleClient)userContext;
                var messageBytes = message.GetBytes();
                var messageString = Encoding.UTF8.GetString(messageBytes);
                Console.WriteLine($"Received message {counterValue}: [{messageString}]");


                using (var filteredMessage = new Message(messageBytes))
                {
                    foreach (KeyValuePair<string, string> prop in message.Properties)
                    {
                        filteredMessage.Properties.Add(prop.Key, prop.Value);
                    }

                    filteredMessage.Properties.Add("MessageType", "Alert");
                    await moduleClient.SendEventAsync("output1", filteredMessage);
                }

                // Indicate that the message treatment is completed.
                return MessageResponse.Completed;
            }
            catch (AggregateException ex)
            {
                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine();
                    Console.WriteLine("Error in sample: {0}", exception);
                }
                // Indicate that the message treatment is not completed.
                var moduleClient = (ModuleClient)userContext;
                return MessageResponse.Abandoned;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("Error in sample: {0}", ex.Message);
                // Indicate that the message treatment is not completed.
                ModuleClient moduleClient = (ModuleClient)userContext;
                return MessageResponse.Abandoned;
            }
        }
    }
}
