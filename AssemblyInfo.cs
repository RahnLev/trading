using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("NinjaTrader Custom")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("NinjaTrader")]
[assembly: AssemblyProduct("NinjaTrader")]
[assembly: AssemblyCopyright("NinjaTrader© 2003-2023 NinjaTrader LLC")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

#if PRODUCTION
[assembly: AssemblyVersion("8.1.6.0")]
#else
[assembly: AssemblyVersion("8.1.7.0")]
#endif

[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.MainAssembly)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
