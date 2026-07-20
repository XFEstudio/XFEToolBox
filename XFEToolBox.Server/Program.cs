using XFEExtension.NetCore.ServerInteractive.Interfaces;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server;

var server = XFEServerBuilder.CreateBuilder()
                             .UseXFEServer()
                             .AddServerCore(XFEServerCoreBuilder.CreateBuilder()
                                                                .UseXFEStandardServerCore<IUserInfo>(option =>
                                                                {
                                                                })
                                                                .Build(option =>
                                                                {
                                                                    option.ServerCoreName = "XFEToolBoxServer";
                                                                    option.MainEntryPoint = "api";
                                                                    option.BindIP();
                                                                }))
                             .Build();

await server.Start();