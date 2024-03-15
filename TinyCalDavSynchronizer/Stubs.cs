using CalDavSynchronizer.Contracts;
using System;
using System.Net;

namespace CalDavSynchronizer
{
    namespace Ui.Options.ViewModels.Mapping
    {
    }

    namespace Ui.ConnectionTests
    {
        public static class ConnectionTester
        {
            public static bool RequiresAutoDiscovery(Uri uri)
            {
                return uri.AbsolutePath == "/" || !uri.AbsolutePath.EndsWith("/");
            }
        }
    }

    namespace Scheduling
    {
        public class SynchronizerFactory
        {
            public static IWebProxy CreateProxy(ProxyOptions options)
            {
                return null;
            }
        }
    }

    public class ComponentContainer
    {
        public static string MessageBoxTitle;
    }
}

