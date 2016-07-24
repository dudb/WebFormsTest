﻿using Fritz.WebFormsTest.Internal;
using Mono.Cecil;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Compilation;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using System.Web.UI;

namespace Fritz.WebFormsTest
{

	/// <summary>
	/// A class that wraps a web application project and provides access to the .NET code contained within
	/// </summary>
	public class WebApplicationProxy : IDisposable
	{

		private static WebApplicationProxy _Instance;
		private readonly Dictionary<Type, string> _LocationTypeMap = new Dictionary<Type, string>();
		private ClientBuildManager _compiler;
		private bool _SkipCrawl = false;
		private bool Initialized = false;
		private bool _SkipPrecompile = true;
		private static HostingEnvironmentWrapper _hostingEnvironment;

		// Identical to SessionStateUtility.SESSION_KEY
		private const String SESSION_KEY = "AspSession";
		private static DummyRegisteredObject _DummyRegisteredObject;

		/// <summary>
		/// Create a proxy for the web application to be inspected
		/// </summary>
		/// <param name="rootFolder">Physical location on disk of the web application source code</param>
		private WebApplicationProxy(string rootFolder, bool skipCrawl, bool skipPrecompile)
		{

			WebRootFolder = rootFolder;
			_SkipCrawl = skipCrawl;
			_SkipPrecompile = skipPrecompile;

		}

		~WebApplicationProxy()
		{
			Dispose(false);
		}


		/// <summary>
		/// Create a Proxy to the Web Application that contains the type submitted
		/// </summary>
		/// <param name="sampleWebType">A sample type from the assembly of the web project to be tested</param>
		/// <param name="options">Options to configure the proxy</param>
		public static void Create(Type sampleWebType, WebApplicationProxyOptions options = null)
		{

			DirectoryInfo webFolder;

			if (options != null && options.WebFolder != null)
			{
				webFolder = new DirectoryInfo(options.WebFolder);
			}
			else {

				webFolder = LocateSourceFolder(sampleWebType);

				Create(webFolder.FullName, options?.SkipCrawl ?? true, options?.SkipPrecompile ?? true);

			}

		}

		[Obsolete("Use the autolocate-enabled signature")]
		public static void Create(string rootFolder, bool skipCrawl = true, bool skipPrecompile = true)
		{
			_Instance = new WebApplicationProxy(rootFolder, skipCrawl, skipPrecompile);

			ReadWebConfig();

			InjectTestValuesIntoHttpRuntime();

			_hostingEnvironment = new HostingEnvironmentWrapper();

			_DummyRegisteredObject = new DummyRegisteredObject();
			HostingEnvironment.RegisterObject(_DummyRegisteredObject);

			SubstituteDummyHttpContext("/");

			_Instance.InitializeInternal();

		}

		/// <summary>
		/// Inspect the application and create mappings for all types
		/// </summary>
		private void InitializeInternal()
		{

			if (Initialized) return;

			// Can this go async?
			_compiler = new ClientBuildManager(@"/",
				WebRootFolder,
				null,
				new ClientBuildManagerParameter()
				{
				});

			SetHttpRuntimeAppDomainAppPath();

			// In larger suites, this may be more cost effective to run
			if (!_SkipPrecompile)
			{
				_compiler.PrecompileApplication();
			}

			ConfigureBuildManager();

			// Async?
			CrawlWebApplication();

			CreateHttpApplication();

			Initialized = true;

		}

		/// <summary>
		/// Force the BuildManager into a state where it thinks we are on a WebHost and already precompiled
		/// </summary>
		private void ConfigureBuildManager()
		{

			var fld = typeof(BuildManager).GetProperty("PreStartInitStage", BindingFlags.Static | BindingFlags.NonPublic);
			var psiEnumValues = typeof(BuildManager).Assembly.GetType("System.Web.Compilation.PreStartInitStage").GetEnumValues();
			fld.SetValue(null, psiEnumValues.GetValue(2), null);

			// Skip Top Level Exceptions, just like we would in precompilation
			var skip = typeof(BuildManager).GetProperty("SkipTopLevelCompilationExceptions", BindingFlags.Static | BindingFlags.NonPublic);
			skip.SetValue(null, true, null);

			// RegularAppRuntimeModeInitialize
			BuildManagerWrapper.RegularAppRuntimeModeInitialize();

		}

		private void SetHttpRuntimeAppDomainAppPath()
		{
			var p = typeof(HttpRuntime).GetField("_appDomainAppPath", BindingFlags.NonPublic | BindingFlags.Instance);
			p.SetValue(HttpRuntimeInstance, WebApplicationProxy.WebPrecompiledFolder);

			//var getAppDomainMethod = typeof(HttpRuntime).GetMethod("GetAppDomainString", BindingFlags.NonPublic | BindingFlags.Static);
			//var appId = getAppDomainMethod.Invoke(null, new object[] { ".appId" });
			var appId = _compiler.GetType().GetField("_appId", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_compiler);

			p = typeof(HttpRuntime).GetField("_appDomainAppId", BindingFlags.NonPublic | BindingFlags.Instance);
			p.SetValue(HttpRuntimeInstance, appId);

			p = typeof(HttpRuntime).GetField("_codegenDir", BindingFlags.NonPublic | BindingFlags.Instance);
			p.SetValue(HttpRuntimeInstance, _compiler.CodeGenDir);

			p = typeof(HttpRuntime).GetField("_DefaultPhysicalPathOnMapPathFailure", BindingFlags.NonPublic | BindingFlags.Static);
			p.SetValue(null, WebApplicationProxy.WebRootFolder);

		}

		/// <summary>
		/// Check if the Proxy is running
		/// </summary>
		public static bool IsInitialized {  get { return _Instance != null ? _Instance.Initialized : false; } }

		/// <summary>
		/// Add some needed configuration to the HttpRuntime so that it thinks we are running in a Web Service
		/// </summary>
		private static void InjectTestValuesIntoHttpRuntime()
		{

			var p = typeof(HttpRuntime).GetField("_appDomainAppVPath", BindingFlags.NonPublic | BindingFlags.Instance);
			p.SetValue(HttpRuntimeInstance, VirtualPathWrapper.Create("/").VirtualPath);

		}

		private static HttpRuntime HttpRuntimeInstance
		{
			get
			{
				var getRunTime = typeof(HttpRuntime).GetField("_theRuntime", BindingFlags.NonPublic | BindingFlags.Static);
				return getRunTime.GetValue(null) as HttpRuntime;
			}
		}

		/// <summary>
		/// Inspect the source code of the application, gathering the page locations and the types at those locations
		/// </summary>
		private void CrawlWebApplication()
		{

			if (_SkipCrawl) return;

			// TODO: Crawl the web application and collect the ASPX / Type mappings from the Page directives

			throw new NotImplementedException("Not yet implemented web application source crawl");

		}

		/// <summary>
		/// Get a fully instantiated Page at the web location specified
		/// </summary>
		/// <param name="location">The web absolute folder location to retrive the Page from</param>
		/// <returns>The Page object from the specified location</returns>
		public static object GetPageByLocation(string location, Action<HttpContext> contextModifiers = null)
		{
			return GetPageByLocation(location, BrowserDefinitions.DEFAULT, contextModifiers);
		}

		/// <summary>
		/// Get a fully instantiated Page at the web location specified
		/// </summary>
		/// <param name="location">The web absolute folder location to retrive the Page from</param>
		/// <returns>The Page object from the specified location</returns>
		public static object GetPageByLocation(string location, HttpBrowserCapabilities browserCaps, Action<HttpContext> contextModifiers = null)
		{

			if (_Instance == null || !_Instance.Initialized) throw new InvalidOperationException("The WebApplicationProxy has not been created and initialized properly");

			var fileLocation = RecalculateLocation(location);

			var returnType = _Instance._compiler.GetCompiledType(fileLocation);
			SubstituteDummyHttpContext(location);

			if (contextModifiers != null) contextModifiers(HttpContext.Current);

			dynamic outObj = Activator.CreateInstance(returnType);

			// Prepare the page for testing
			if (outObj is Page) ((Page)outObj).PrepareForTest();

			CompleteHttpContext(HttpContext.Current);

			return outObj;


		}

		/// <summary>
		/// Handle location requested if the ASPX is missing
		/// </summary>
		/// <param name="location"></param>
		public static string RecalculateLocation(string location)
		{

			var fullLocation = location.Replace('/', '\\');
      if (fullLocation.StartsWith("\\")) fullLocation = fullLocation.Substring(1);
      fullLocation = Path.Combine(WebRootFolder, fullLocation);
      if (new FileInfo(fullLocation).Exists)
      {
        // File exists, no more monkey business needed
        return location;
      }

			if (!fullLocation.ToLowerInvariant().Contains("aspx"))
			{
				location = location + ".aspx";
				return RecalculateLocation(location);
			}

			// Split the location and examine each location
			var segments = location.Split('/');
			var aspxSegment = 0;
			for (var i=segments.Count()-1; i>=0; i--)
			{
				if (segments[i].EndsWith(".aspx"))
				{
					aspxSegment = i;
					break;
				}
			}

			if (aspxSegment == 0) { throw new FileNotFoundException($"Unable to find the file at {location}"); }

			segments[aspxSegment] = segments[aspxSegment].Replace(".aspx", "");
			segments[aspxSegment - 1] += ".aspx";
			location = string.Join("/", segments.Select((s,i) => i<=aspxSegment-1?s:""));
      while (location.EndsWith("/")) location = location.Substring(0, location.Length - 1);
			return RecalculateLocation(location);

		}

		/// <summary>
		/// Get a fully instantiated Page at the web location specified
		/// </summary>
		/// <typeparam name="T">The type of the Page to fetch</typeparam>
		/// <param name="location">The web absolute folder location to retrive the Page from</param>
		/// <returns>A strongly-typed Page object from the specified location</returns>
		public static T GetPageByLocation<T>(string location, Action<HttpContext> contextModifiers = null) where T : Page
		{

			return GetPageByLocation(location, contextModifiers) as T;

		}

		/// <summary>
		/// Get a fully instantiated Page at the web location specified
		/// </summary>
		/// <typeparam name="T">The type of the Page to fetch</typeparam>
		/// <param name="location">The web absolute folder location to retrive the Page from</param>
		/// <returns>A strongly-typed Page object from the specified location</returns>
		public static T GetPageByLocation<T>(string location, HttpBrowserCapabilities browserCaps, Action<HttpContext> contextModifiers = null) where T : Page
		{

			return GetPageByLocation(location, browserCaps, contextModifiers) as T;

		}

		public static T GetPageByType<T>() where T : Page
		{

			if (_Instance._SkipCrawl) throw new InvalidOperationException("Unable to fetch by type because the project has not been indexed");

			var locn = _Instance._LocationTypeMap[typeof(T)];

			return GetPageByLocation<T>(locn);

		}

		public static object GetPageByType(Type pageType)
		{

			if (_Instance._SkipCrawl) throw new InvalidOperationException("Unable to fetch by type because the project has not been indexed");

			var locn = _Instance._LocationTypeMap[pageType];

			return GetPageByLocation(locn);

		}

		/// <summary>
		/// This is the application object created typically by the "global.asax.cs" class
		/// </summary>
		public static HttpApplication Application
		{
			get; private set;
		}

		public static Cache Cache
		{
			get
			{

				// if (HttpContext.Current == null) SubstituteDummyHttpContext();
				return HttpContext.Current.Cache;

			}
		}

		/// <summary>
		/// The physical location of the web application on disk.  c:\dev\MyWebApp
		/// </summary>
		public static string WebRootFolder
		{
			get; private set;
		}

		/// <summary>
		/// Gets a new session and associates it with HttpContext.Current
		/// </summary>
		/// <returns></returns>
		public static HttpSessionState GetNewSession(Dictionary<string, object> initialItems = null)
		{

			var sessionId = Guid.NewGuid().ToString();
			var sessionContainer = new TestHttpSessionStateContainer(sessionId);

			var ctor = typeof(HttpSessionState).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(IHttpSessionState) }, null);
			var newSession = ctor.Invoke(new object[] { sessionContainer }) as HttpSessionState;

			if (initialItems != null)
			{
				foreach (var item in initialItems)
				{
					newSession.Add(item.Key, item.Value);
				}
			}

			return newSession;


		}

		public static void DisposeIt()
		{
			_Instance.Dispose();
		}

		public void Dispose()
		{
			Dispose(true);
		}

		protected virtual void Dispose(bool isDisposing)
		{

			if (isDisposing) GC.SuppressFinalize(this);

			// Clean up the target folder
			try {
				// Directory.Delete(WebApplicationRootFolder, true);
			} catch (DirectoryNotFoundException)
			{
				// Its ok...  its already gone!
			}
		}

		/// <summary>
		/// Swap in a dummy HttpContext for HttpContext.Current, valid for current thread only
		/// </summary>
		internal static void SubstituteDummyHttpContext(string location, HttpBrowserCapabilities caps = null)
		{

			var workerRequest = new TestHttpWorkerRequest(location);
			var testContext = new HttpContext(workerRequest);

			// BrowserCapabilities
			caps = caps ?? BrowserDefinitions.DEFAULT;
			testContext.Request.SetBrowserCaps(caps);

			testContext.Items["IsInTestMode"] = true;

			HttpContext.Current = testContext;

		}

		private static void CompleteHttpContext(HttpContext ctx)
		{

			// Socket State
			ctx.GetType().GetProperty("WebSocketTransitionState", BindingFlags.NonPublic | BindingFlags.Instance)
				.SetValue(ctx, (byte)2, null);

		}

		public static string WebPrecompiledFolder
		{
			get { return _Instance._compiler.CodeGenDir; }
		}

		#region Private Methods

		/// <summary>
		/// Get the HttpApplication Type (typically named Global) from the application
		/// </summary>
		/// <returns></returns>
		internal static Type GetHttpApplicationType()
		{

			// Force the web application to be loaded 
			_Instance._compiler.GetCompiledType("/Default.aspx");

			// Search the AppDomain of loaded types that inherit from HttpApplication, starting with "Global"
			Type httpApp = typeof(HttpApplication);

			Type outType;

			// Blacklist assembly name prefixes
			var blacklist = new string[] { "Microsoft.", "System", "mscorlib", "xunit." };

			var assembliesToScan = AppDomain.CurrentDomain.GetAssemblies().Where(a => !blacklist.Any(b => a.FullName.StartsWith(b))).ToArray();
			var theAssembly = assembliesToScan.FirstOrDefault(a =>
			{
				bool outValue = false;
				try
				{
					outValue = a.GetTypes().FirstOrDefault(t => t.FullName.EndsWith(".Global") && (t.BaseType == httpApp)) != null;
				}
				catch
				{
					outValue = false;
				}
				return outValue;
			});
			outType = theAssembly.GetTypes().First(t => t.BaseType == httpApp);

			return outType;

		}

		private static DirectoryInfo LocateSourceFolder(Type sampleWebType)
		{
			DirectoryInfo webFolder;
			var parms = new ReaderParameters { ReadSymbols = true };
			var assemblyDef = AssemblyDefinition.ReadAssembly(new Uri(sampleWebType.Assembly.CodeBase).LocalPath);

			byte[] debugHeader;
			var img = assemblyDef.MainModule.GetDebugHeader(out debugHeader);
			var pdbFilename = Encoding.ASCII.GetString(debugHeader.Skip(24).Take(debugHeader.Length - 25).ToArray());
			var pdbFile = new FileInfo(pdbFilename);
			webFolder = pdbFile.Directory;

			while ((webFolder.GetFiles("*.csproj").Length == 0 && webFolder.GetFiles("*.vbproj").Length == 0))
			{
				webFolder = webFolder.Parent;
			}

			return webFolder;
		}


		private static void TriggerApplicationStart(HttpApplication app)
		{

			var mi = app.GetType().GetMethod("Application_Start", BindingFlags.Instance | BindingFlags.NonPublic);
			mi.Invoke(app, new object[] { app, EventArgs.Empty });

		}

		private static void CreateHttpApplication()
		{

			// Create the Global / HttpApplication object
			Type appType = GetHttpApplicationType();
			WebApplicationProxy.Application = Activator.CreateInstance(appType) as HttpApplication;

			// Create the HttpApplicationState
			Type asType = typeof(HttpApplicationState);
			var appState = asType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null)
				.Invoke(new object[] { }) as HttpApplicationState;

			// Inject the application state
			var theField = typeof(HttpApplication).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
			theField.SetValue(WebApplicationProxy.Application, appState);

			TriggerApplicationStart(WebApplicationProxy.Application);



		}

		private static void ReadWebConfig()
		{

			AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", Path.Combine(WebRootFolder, "web.config"));
//      WebConfigurationManager.OpenWebConfiguration(Path.Combine(WebRootFolder, "web.con‌​fig"));

		}

		#endregion

		internal class DummyRegisteredObject : IRegisteredObject
		{
			public void Stop(bool immediate)
			{
				return;
			}
		}

	}



}
