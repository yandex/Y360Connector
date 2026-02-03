using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Y360OutlookConnector.Synchronization;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnectorUnitTests
{
    [TestClass]
    public class UserEmailServiceTests
    {
        private TestHttpMessageHandler _testHttpHandler;
        private HttpClient _httpClient;
        private UserEmailService _userEmailService;

        [TestInitialize]
        public void Setup()
        {
            _testHttpHandler = new TestHttpMessageHandler();
            _httpClient = new HttpClient(_testHttpHandler);
            _userEmailService = new UserEmailService(_httpClient);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _httpClient?.Dispose();
            _userEmailService?.ClearCache();
            EmailAddress.SetUserEmailService(null);
        }

        #region Core API Tests

        [TestMethod]
        public async Task GetUserEmailsAsync_ValidResponse_ReturnsOnlyValidatedEmails()
        {
            // Arrange
            var apiResponse = @"{
                ""users"": [
                    {
                        ""addresses"": [
                            {
                                ""address"": ""user@ya.ru"",
                                ""native"": true,
                                ""validated"": true
                            },
                            {
                                ""address"": ""user@yandex.by"",
                                ""native"": true,
                                ""validated"": true
                            },
                            {
                                ""address"": ""user-fake@invalid.com"",
                                ""native"": false,
                                ""validated"": false
                            }
                        ]
                    }
                ]
            }";
            _testHttpHandler.SetResponse(HttpStatusCode.OK, apiResponse);

            // Act
            var result = await _userEmailService.GetUserEmailsAsync("test-token");

            // Assert
            Assert.AreEqual(2, result.Count);
            var emails = new List<string>();
            foreach (var email in result) emails.Add(email.ToString());
            CollectionAssert.Contains(emails, "user@ya.ru");
            CollectionAssert.Contains(emails, "user@yandex.by");
            CollectionAssert.DoesNotContain(emails, "user-fake@invalid.com");
        }

        [TestMethod]
        public async Task GetUserEmailsAsync_HttpError_ThrowsException()
        {
            // Arrange
            _testHttpHandler.SetResponse(HttpStatusCode.Unauthorized, "Unauthorized");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _userEmailService.GetUserEmailsAsync("invalid-token"));
        }

        [TestMethod]
        public async Task GetUserEmailsAsync_Caching_ReturnsCachedResult()
        {
            // Arrange
            var apiResponse = @"{
                ""users"": [
                    {
                        ""addresses"": [
                            {
                                ""address"": ""user@ya.ru"",
                                ""native"": true,
                                ""validated"": true
                            }
                        ]
                    }
                ]
            }";
            _testHttpHandler.SetResponse(HttpStatusCode.OK, apiResponse);

            // Act - Two calls
            var result1 = await _userEmailService.GetUserEmailsAsync("test-token");
            var result2 = await _userEmailService.GetUserEmailsAsync("test-token");
            // Assert
            Assert.AreEqual(result1.Count, result2.Count);

            // Verify HTTP call was made only once
            Assert.AreEqual(1, _testHttpHandler.RequestCount);
        }

        #endregion

        #region Email Comparison Tests

        [TestMethod]
        public void IsUserEmail_WithCachedEmails_ReturnsCorrectResult()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru"),
                EmailAddress.Parse("user@yandex.by")
            });

            // Act & Assert
            Assert.IsTrue(_userEmailService.IsUserEmail("user@ya.ru"));
            Assert.IsTrue(_userEmailService.IsUserEmail("user@yandex.by"));
            Assert.IsFalse(_userEmailService.IsUserEmail("other@example.com"));
            Assert.IsFalse(_userEmailService.IsUserEmail(""));
            Assert.IsFalse(_userEmailService.IsUserEmail(null));
        }

        [TestMethod]
        public void AreEmailsSame_BothUserEmails_ReturnsTrue()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru"),
                EmailAddress.Parse("user@yandex.by")
            });

            // Act & Assert
            Assert.IsTrue(_userEmailService.AreEmailsSame("user@ya.ru", "user@yandex.by"));
            Assert.IsTrue(_userEmailService.AreEmailsSame("user@yandex.by", "user@ya.ru"));
        }

        [TestMethod]
        public void AreEmailsSame_OneUserEmail_ReturnsFalse()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru")
            });

            // Act & Assert
            Assert.IsFalse(_userEmailService.AreEmailsSame("user@ya.ru", "other@example.com"));
            Assert.IsFalse(_userEmailService.AreEmailsSame("other@example.com", "user@ya.ru"));
        }

        [TestMethod]
        public void ClearCache_RemovesCachedEmails()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru")
            });
            Assert.IsTrue(_userEmailService.IsUserEmail("user@ya.ru"));

            // Act
            _userEmailService.ClearCache();

            // Assert
            Assert.IsFalse(_userEmailService.IsUserEmail("user@ya.ru"));
        }

        #endregion

        #region EmailAddress Integration Tests

        [TestMethod]
        public void EmailAddress_AreSame_WithService_UserEmails_ReturnsTrue()
        {
            // Arrange
            var mockService = new MockUserEmailService();
            mockService.SetUserEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru"),
                EmailAddress.Parse("user@yandex.by")
            });
            EmailAddress.SetUserEmailService(mockService);

            // Act & Assert
            Assert.IsTrue(EmailAddress.AreSame("user@ya.ru", "user@yandex.by"));
            Assert.IsTrue(EmailAddress.AreSame("user@yandex.by", "user@ya.ru"));
        }

        [TestMethod]
        public void EmailAddress_AreSame_WithoutService_FallbackToBasicComparison()
        {
            // Arrange
            EmailAddress.SetUserEmailService(null);

            // Act & Assert
            Assert.IsTrue(EmailAddress.AreSame("user@ya.ru", "user@ya.ru"));
            Assert.IsFalse(EmailAddress.AreSame("user@ya.ru", "user@yandex.by"));
        }

        [TestMethod]
        public void EmailAddress_AreSame_WithServiceError_FallbackToBasicComparison()
        {
            // Arrange
            var mockService = new MockUserEmailService();
            mockService.SetShouldThrowException(true);
            EmailAddress.SetUserEmailService(mockService);

            // Act & Assert
            Assert.IsTrue(EmailAddress.AreSame("user@ya.ru", "user@ya.ru"));
            Assert.IsFalse(EmailAddress.AreSame("user@ya.ru", "user@yandex.by"));
        }
        [TestMethod]
        public void EmailAddress_AreSame_StringOverload_WithDomainAliasesParameter_IgnoresParameter()
        {
            // Arrange
            var mockService = new MockUserEmailService();
            mockService.SetUserEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru"),
                EmailAddress.Parse("user@yandex.by")
            });
            EmailAddress.SetUserEmailService(mockService);

            // Act & Assert - The domainAliases parameter should be ignored
            Assert.IsTrue(EmailAddress.AreSame("user@ya.ru", "user@yandex.by", new string[][] { }));
            Assert.IsTrue(EmailAddress.AreSame("user@ya.ru", "user@yandex.by", null));
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public async Task GetUserEmailsAsync_MalformedEmailAddresses_IgnoresInvalidOnes()
        {
            // Arrange
            var apiResponse = @"{
                ""users"": [
                    {
                        ""addresses"": [
                            {
                                ""address"": ""valid@ya.ru"",
                                ""native"": true,
                                ""validated"": true
                            },
                            {
                                ""address"": ""invalid-email"",
                                ""native"": true,
                                ""validated"": true
                            },
                            {
                                ""address"": """",
                                ""native"": true,
                                ""validated"": true
                            }
                        ]
                    }
                ]
            }";
            _testHttpHandler.SetResponse(HttpStatusCode.OK, apiResponse);

            // Act
            var result = await _userEmailService.GetUserEmailsAsync("test-token");

            // Assert
            Assert.AreEqual(1, result.Count); // Only valid email
            Assert.AreEqual("valid@ya.ru", result[0].ToString());
        }

        [TestMethod]
        public async Task GetUserEmailsAsync_NetworkTimeout_ThrowsException()
        {
            // Arrange
            _testHttpHandler.SetException(new TaskCanceledException("Request timeout"));

            // Act & Assert
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(
                () => _userEmailService.GetUserEmailsAsync("test-token"));
        }

        [TestMethod]
        public void IsUserEmail_CaseInsensitive_WorksCorrectly()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("User@YA.RU"),
                EmailAddress.Parse("user@yandex.by")
            });

            // Act & Assert
            Assert.IsTrue(_userEmailService.IsUserEmail("user@ya.ru"));
            Assert.IsTrue(_userEmailService.IsUserEmail("USER@YA.RU"));
            Assert.IsTrue(_userEmailService.IsUserEmail("User@ya.ru"));
        }

        [TestMethod]
        public void IsUserEmail_WithDotsInNameId_NormalizedCorrectly()
        {
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user.name@ya.ru")
            });

            // Act & Assert
            Assert.IsTrue(_userEmailService.IsUserEmail("user.name@ya.ru"));
            Assert.IsTrue(_userEmailService.IsUserEmail("USER.NAME@YA.RU"));
            Assert.IsTrue(_userEmailService.IsUserEmail("user-name@ya.ru")); // Normalized form
        }

        [TestMethod]
        public async Task GetUserEmailsAsync_ConcurrentCalls_OnlyOneHttpRequest()
        {
            // Arrange
            var apiResponse = @"{
                ""users"": [
                    {
                        ""addresses"": [
                            {
                                ""address"": ""user@ya.ru"",
                                ""native"": true,
                                ""validated"": true
                            }
                        ]
                    }
                ]
            }";
            _testHttpHandler.SetResponse(HttpStatusCode.OK, apiResponse);

            // Act - Make multiple concurrent calls
            var tasks = new List<Task<List<EmailAddress>>>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_userEmailService.GetUserEmailsAsync("test-token"));
            }
            await Task.WhenAll(tasks);
            // Assert
            foreach (var task in tasks)
            {
                Assert.AreEqual(1, task.Result.Count);
            }

            // Verify only one HTTP request was made
            Assert.AreEqual(1, _testHttpHandler.RequestCount);
        }

        #endregion

        #region Performance Tests

        [TestMethod]
        public void IsUserEmail_Performance_WithLargeCache()
        {
            // Arrange - Create a large cache
            var cachedEmails = new List<EmailAddress>();
            for (int i = 0; i < 1000; i++)
            {
                cachedEmails.Add(EmailAddress.Parse($"user{i}@ya.ru"));
            }
            SetCachedEmails(cachedEmails);

            // Act - Measure performance
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                _userEmailService.IsUserEmail($"user{i}@ya.ru");
            }
            stopwatch.Stop();

            // Assert - Should complete within reasonable time
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000,
                $"Performance test took {stopwatch.ElapsedMilliseconds}ms, expected less than 1000ms");
        }

        [TestMethod]
        public void CacheExpiration_Simulation_WorksCorrectly()
        {
            // Arrange
            SetCachedEmails(new List<EmailAddress>
            {
                EmailAddress.Parse("user@ya.ru")
            });

            // Simulate expired cache
            var lastUpdateField = typeof(UserEmailService).GetField("_lastCacheUpdate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            lastUpdateField.SetValue(_userEmailService, DateTime.UtcNow.AddHours(-2));

            // Act
            var result = _userEmailService.IsUserEmail("user@ya.ru");

            // Assert - Should return false because cache is expired
            Assert.IsFalse(result);
        }

        #endregion

        #region Helper Methods

        private void SetCachedEmails(List<EmailAddress> emails)
        {
            var emailsField = typeof(UserEmailService).GetField("_cachedUserEmails",
                 System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var lastUpdateField = typeof(UserEmailService).GetField("_lastCacheUpdate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Normalize emails before storing them (same as the service does)
            var normalizedEmails = emails.Select(email => email.Normalize()).ToList();
            emailsField.SetValue(_userEmailService, normalizedEmails);
            lastUpdateField.SetValue(_userEmailService, DateTime.UtcNow); //Set to current time to make cache valid
        }

        #endregion
    }

    /// <summary>
    /// Custom HttpMessageHandler for testing
    /// </summary>
    public class TestHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage _response;
        private Exception _exception;
        public int RequestCount { get; private set; }

        public void SetResponse(HttpStatusCode statusCode, string content)
        {
            _response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            };
            _exception = null;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
            _response = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;

            if (_exception != null)
            {
                throw _exception;
            }

            return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Mock implementation of IUserEmailService for testing
    /// </summary>
    public class MockUserEmailService : IUserEmailService
    {
        private List<EmailAddress> _userEmails;
        private bool _shouldThrowException;

        public MockUserEmailService()
        {
            _userEmails = new List<EmailAddress>();
        }

        public void SetUserEmails(List<EmailAddress> emails)
        {
            _userEmails = emails ?? new List<EmailAddress>();
        }
        public void SetShouldThrowException(bool shouldThrow)
        {
            _shouldThrowException = shouldThrow;
        }

        public Task<List<EmailAddress>> GetUserEmailsAsync(string accessToken)
        {
            return Task.FromResult(_userEmails);
        }

        public bool IsUserEmail(string emailToCheck)
        {
            if (_shouldThrowException)
            {
                throw new Exception("Mock exception");
            }

            if (string.IsNullOrEmpty(emailToCheck))
            {
                return false;
            }

            var normalizedEmailToCheck = NormalizeEmail(emailToCheck);
            return _userEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmailToCheck);
        }

        public bool AreEmailsSame(string email1, string email2)
        {
            if (_shouldThrowException)
            {
                throw new Exception("Mock exception");
            }

            if (string.IsNullOrEmpty(email1) || string.IsNullOrEmpty(email2))
            {
                return false;
            }

            var normalizedEmail1 = NormalizeEmail(email1);
            var normalizedEmail2 = NormalizeEmail(email2);

            var email1IsUser = _userEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmail1);
            var email2IsUser = _userEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmail2);

            if (email1IsUser && email2IsUser)
            {
                return true;
            }

            return normalizedEmail1 == normalizedEmail2;
        }

        public void ClearCache()
        {
            _userEmails.Clear();
        }

        private string NormalizeEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return string.Empty;
            }

            var parsed = EmailAddress.Parse(email.Trim());
            var normalized = parsed.Normalize();
            return $"{normalized.NameId.ToLowerInvariant()}@{normalized.Domain.ToLowerInvariant()}";
        }
    }
}
