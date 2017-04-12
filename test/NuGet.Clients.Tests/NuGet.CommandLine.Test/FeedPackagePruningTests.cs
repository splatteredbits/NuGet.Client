using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.CommandLine.Test
{
    public class FeedPackagePruningTests
    {
        [Fact]
        public async Task FeedPackagePruning_GivenThatAV3FeedPrunesAPackageDuringRestoreVerifyRestoreRecovers()
        {
            // Arrange
            using (var server = new MockServer())
            using (var pathContext = new SimpleTestPathContext())
            {
                // Set up solution, project, and packages
                var solution = new SimpleTestSolutionContext(pathContext.SolutionRoot);

                var projectA = SimpleTestProjectContext.CreateNETCore(
                    "a",
                    pathContext.SolutionRoot,
                    NuGetFramework.Parse("net45"));

                var packageX = new SimpleTestPackageContext()
                {
                    Id = "x",
                    Version = "1.0.0"
                };

                projectA.AddPackageToAllFrameworks(packageX);

                solution.Projects.Add(projectA);
                solution.Create(pathContext.SolutionRoot);

                // Server setup
                var indexJson = Util.CreateIndexJson();
                Util.AddFlatContainerResource(indexJson, server);
                Util.AddRegistrationResource(indexJson, server);

                var serverRepoPath = Path.Combine(pathContext.WorkingDirectory, "serverPackages");

                await SimpleTestPackageUtility.CreateFolderFeedV3(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                server.Get.Add("/", request =>
                {
                    return ServerHandlerV3(request, server, indexJson, serverRepoPath);
                });

                server.Start();

                // Act
                var r = Util.RestoreSolution(pathContext);

                // Assert
                Assert.True(File.Exists(projectA.AssetsFileOutputPath), r.Item2);
            }
        }

        private Action<HttpListenerResponse> ServerHandlerV3(
            HttpListenerRequest request,
            MockServer server,
            JObject indexJson,
            string repositoryPath)
        {
            try
            {
                var path = server.GetRequestUrlAbsolutePath(request);
                var parts = request.Url.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (path == "/index.json")
                {
                    return new Action<HttpListenerResponse>(response =>
                    {
                        response.StatusCode = 200;
                        response.ContentType = "text/javascript";
                        MockServer.SetResponseContent(response, indexJson.ToString());
                    });
                }
                else if (path.StartsWith("/flat/") && path.EndsWith("/index.json"))
                {
                    return new Action<HttpListenerResponse>(response =>
                    {
                        response.ContentType = "text/javascript";

                        var versionsJson = JObject.Parse(@"{ ""versions"": [] }");
                        var array = versionsJson["versions"] as JArray;

                        var id = parts[parts.Length - 2];

                        foreach (var pkg in LocalFolderUtility.GetPackagesV3(repositoryPath, id, new TestLogger()))
                        {
                            array.Add(pkg.Identity.Version.ToNormalizedString());
                        }

                        MockServer.SetResponseContent(response, versionsJson.ToString());
                    });
                }
                else if (path.StartsWith("/flat/") && path.EndsWith(".nupkg"))
                {
                    var file = new FileInfo(Path.Combine(repositoryPath, parts.Last()));

                    if (file.Exists)
                    {
                        return new Action<HttpListenerResponse>(response =>
                        {
                            response.ContentType = "application/zip";
                            using (var stream = file.OpenRead())
                            {
                                var content = stream.ReadAllBytes();
                                MockServer.SetResponseContent(response, content);
                            }
                        });
                    }
                    else
                    {
                        return new Action<HttpListenerResponse>(response =>
                        {
                            response.StatusCode = 404;
                        });
                    }
                }
                else if (path == "/nuget")
                {
                    return new Action<HttpListenerResponse>(response =>
                    {
                        response.StatusCode = 200;
                    });
                }

                throw new Exception("This test needs to be updated to support: " + path);
            }
            catch (Exception)
            {
                // Debug here
                throw;
            }
        }
    }
}
