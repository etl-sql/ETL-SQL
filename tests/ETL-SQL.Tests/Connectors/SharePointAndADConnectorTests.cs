using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Connectors;
using ETL_SQL.Data;
using Moq;
using System.Text;
using ETL_SQL.Common;
using ETL_SQL.Services;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Category", "Connectors")]
    public class SharePointAndADConnectorTests
    {
        private Mock<IExecutionContext> CreateMockContext()
        {
            var mockLogger = new Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object);
            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);
            return mockContext;
        }

        // ── Active Directory Connector Tests ──────────────────────────────────

        [Fact]
        public void ActiveDirectoryConnector_Metadata_IsCorrect()
        {
            var connector = new ActiveDirectoryConnector();
            Assert.Equal("ACTIVE_DIRECTORY", connector.Name);
            Assert.Contains("AD", connector.Aliases);
            Assert.Contains("LDAP", connector.Aliases);
            Assert.NotEmpty(connector.GetHelp());
        }

        [Theory]
        [InlineData("users", "(&(objectCategory=person)(objectClass=user))")]
        [InlineData("user", "(&(objectCategory=person)(objectClass=user))")]
        [InlineData("groups", "(objectClass=group)")]
        [InlineData("group", "(objectClass=group)")]
        [InlineData("computers", "(objectClass=computer)")]
        [InlineData("contacts", "(objectClass=contact)")]
        [InlineData("custom", "(objectClass=custom)")]
        public void ActiveDirectoryConnector_ResolveLdapFilter_MapsVirtualTablesCorrectly(string context, string expectedFilter)
        {
            var mockContext = CreateMockContext();
            
            var options = new Dictionary<string, string>
            {
                { "FILTER_CONTEXT", context }
            };

            var connector = new ActiveDirectoryConnector(mockContext.Object, "ldap://localhost", options);
            var resolvedFilter = connector.ResolveLdapFilter();

            Assert.Equal(expectedFilter, resolvedFilter);
        }

        [Fact]
        public void ActiveDirectoryConnector_ResolveLdapFilter_UsesCustomOption()
        {
            var mockContext = CreateMockContext();

            var options = new Dictionary<string, string>
            {
                { "FILTER", "(sAMAccountName=admin*)" }
            };

            var connector = new ActiveDirectoryConnector(mockContext.Object, "ldap://localhost", options);
            var resolvedFilter = connector.ResolveLdapFilter();

            Assert.Equal("(sAMAccountName=admin*)", resolvedFilter);
        }

        [Fact]
        public void ActiveDirectoryConnector_BuildConnectionString_BuildsLdapUrls()
        {
            var connector = new ActiveDirectoryConnector();
            var props = new Dictionary<string, string>
            {
                { "HOST", "corp.company.com" },
                { "PORT", "389" },
                { "BASE_DN", "DC=corp,DC=company,DC=com" }
            };

            var connStr = connector.BuildConnectionString(props);
            Assert.Equal("ldap://corp.company.com:389/DC=corp,DC=company,DC=com", connStr);
        }

        // ── SharePoint Connector Tests ────────────────────────────────────────

        [Fact]
        public void SharePointConnector_Metadata_IsCorrect()
        {
            var connector = new SharePointConnector();
            Assert.Equal("SHAREPOINT", connector.Name);
            Assert.Contains("SP", connector.Aliases);
            Assert.NotEmpty(connector.GetHelp());
        }

        [Theory]
        [InlineData("", "/sites/Finance/Shared Documents")]
        [InlineData("incoming", "/sites/Finance/Shared Documents/incoming")]
        [InlineData("incoming/sales.csv", "/sites/Finance/Shared Documents/incoming/sales.csv")]
        [InlineData("/sites/Finance/Shared Documents/incoming", "/sites/Finance/Shared Documents/incoming")]
        public void SharePointConnector_GetServerRelativeUrl_BuildsCorrectUrls(string path, string expectedRelativeUrl)
        {
            var mockContext = CreateMockContext();

            var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", null);
            var relativeUrl = connector.GetServerRelativeUrl(path);

            Assert.Equal(expectedRelativeUrl, relativeUrl);
        }

        // ── SharePoint Functional Mock HTTP Tests ──────────────────────────────

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = req => new HttpResponseMessage(HttpStatusCode.OK);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                System.Console.WriteLine($"[MOCK-HANDLER-PRINT] requestUri={request.RequestUri}");
                return Task.FromResult(Handler(request));
            }
        }

        [Fact]
        public async Task SharePointConnector_ListFilesAsync_ParsesRESTResponse()
        {
            var mockContext = CreateMockContext();

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                var uriStr = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
                Assert.Contains("_api/web/GetFolderByServerRelativeUrl", uriStr);
                Assert.Contains("Shared Documents", uriStr);
                Assert.Contains("/Files", uriStr);
                
                var payload = new
                {
                    value = new[]
                    {
                        new
                        {
                            Name = "file1.txt",
                            ServerRelativeUrl = "/sites/Finance/Shared Documents/file1.txt",
                            Length = 100,
                            TimeLastModified = "2026-05-28T12:00:00Z"
                        },
                        new
                        {
                            Name = "file2.csv",
                            ServerRelativeUrl = "/sites/Finance/Shared Documents/file2.csv",
                            Length = 200,
                            TimeLastModified = "2026-05-28T12:30:00Z"
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            };

            var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", null, mockHandler);
            var files = await connector.ListFilesAsync("").ToListAsync();

            Assert.Equal(2, files.Count);
            Assert.Equal("file1.txt", files[0].Name);
            Assert.Equal("/sites/Finance/Shared Documents/file1.txt", files[0].FullPath);
            Assert.Equal(100, files[0].Size);
            Assert.Equal("file2.csv", files[1].Name);
            Assert.Equal(200, files[1].Size);
        }

        [Fact]
        public async Task SharePointConnector_UploadFileAsync_SendsDataToRestEndpoint()
        {
            var mockContext = CreateMockContext();

            string tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, "hello sharepoint file content");

            try
            {
                var mockHandler = new MockHttpMessageHandler();
                mockHandler.Handler = req =>
                {
                    var uriStr = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
                    Assert.Equal(HttpMethod.Post, req.Method);
                    Assert.Contains("Files/Add(url='remote.txt', overwrite=true)", uriStr);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                };

                var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", null, mockHandler);
                await connector.UploadFileAsync(tempFile, "remote.txt", overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SharePointConnector_DownloadFileAsync_SavesFileContent()
        {
            var mockContext = CreateMockContext();

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                var uriStr = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
                Assert.Contains("GetFileByServerRelativeUrl", uriStr);
                Assert.Contains("Shared Documents/remote.txt", uriStr);
                Assert.Contains("$value", uriStr);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("some sharepoint data stream")
                };
            };

            var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", null, mockHandler);
            string destFile = Path.GetTempFileName();

            try
            {
                await connector.DownloadFileAsync("remote.txt", destFile, overwrite: true);
                var content = File.ReadAllText(destFile);
                Assert.Equal("some sharepoint data stream", content);
            }
            finally
            {
                if (File.Exists(destFile)) File.Delete(destFile);
            }
        }

        [Fact]
        public async Task SharePointConnector_DeleteFileAsync_IssuesDeleteRequest()
        {
            var mockContext = CreateMockContext();

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                var uriStr = Uri.UnescapeDataString(req.RequestUri?.ToString() ?? "");
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("DELETE", req.Headers.GetValues("X-HTTP-Method").First());
                Assert.Contains("GetFileByServerRelativeUrl", uriStr);
                Assert.Contains("Shared Documents/remote.txt", uriStr);
                return new HttpResponseMessage(HttpStatusCode.OK);
            };

            var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", null, mockHandler);
            await connector.DeleteFileAsync("remote.txt");
        }

        [Fact]
        public async Task SharePointConnector_ReadBatches_ParsesSharePointListRESTResponse()
        {
            var mockContext = CreateMockContext();

            var mockHandler = new MockHttpMessageHandler();
            mockHandler.Handler = req =>
            {
                Assert.Contains("/lists/GetByTitle('Tasks')/items", req.RequestUri?.ToString());
                
                var payload = new
                {
                    value = new[]
                    {
                        new
                        {
                            Title = "First task",
                            Priority = "High",
                            PercentComplete = 100
                        },
                        new
                        {
                            Title = "Second task",
                            Priority = "Normal",
                            PercentComplete = 50
                        }
                    }
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            };

            var options = new Dictionary<string, string> { { "LIST_NAME", "Tasks" } };
            var connector = new SharePointConnector(mockContext.Object, "https://company.sharepoint.com/sites/Finance", options, mockHandler);
            
            var batches = await connector.ReadBatches().ToListAsync();

            Assert.Single(batches);
            var table = batches[0];
            Assert.Equal(2, table.Rows.Count);
            Assert.Contains("Title", table.ColumnNames);
            Assert.Contains("Priority", table.ColumnNames);
            Assert.Contains("PercentComplete", table.ColumnNames);
            Assert.Equal("First task", table.Rows[0]["Title"]);
            Assert.Equal("Second task", table.Rows[1]["Title"]);
        }
    }
}
