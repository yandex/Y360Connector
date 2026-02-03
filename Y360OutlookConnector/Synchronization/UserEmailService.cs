using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using Newtonsoft.Json;
using Y360OutlookConnector.Configuration;
using Y360OutlookConnector.Utilities;

namespace Y360OutlookConnector.Synchronization
{
    public class UserEmailService : IUserEmailService
    {
        private static readonly ILog s_logger = LogManager.GetLogger(typeof(UserEmailService));

        private readonly HttpClient _httpClient;
        private readonly string _apiEndpoint = "https://cloud-api.yandex.ru/v1/calendar/user-info";

        // In-memory storage
        private List<EmailAddress> _cachedUserEmails;
        private DateTime _lastCacheUpdate;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(1);
        private readonly object _cacheLock = new object();
        private readonly SemaphoreSlim _refreshSemaphore = new SemaphoreSlim(1, 1);

        public UserEmailService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<List<EmailAddress>> GetUserEmailsAsync(string accessToken)
        {
            if (string.IsNullOrEmpty(accessToken))
                throw new ArgumentException("Access token cannot be null or empty", nameof(accessToken));

            await _refreshSemaphore.WaitAsync();
            try
            {
                if (IsCacheValid())
                {
                    s_logger.Debug($"Returning {_cachedUserEmails.Count} cached user emails");
                    return _cachedUserEmails;
                }

                s_logger.Info("Cache expired or empty, fetching user emails from API");
                return await RefreshCacheAsync(accessToken);
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }

        private async Task<List<EmailAddress>> RefreshCacheAsync(string accessToken)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, _apiEndpoint))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

                    s_logger.Debug($"Making API request to: {_apiEndpoint}");
                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var jsonContent = await response.Content.ReadAsStringAsync();
                    s_logger.Debug($"Received API response: {jsonContent}");

                    var userEmailsResponse = JsonConvert.DeserializeObject<UserEmailsResponse>(jsonContent);

                    var userEmails = new List<EmailAddress>();

                    foreach (var user in userEmailsResponse.Users)
                    {
                        foreach (var address in user.Addresses)
                        {
                            if (address.IsValidated)
                            {
                                var emailAddress = EmailAddress.Parse(address.Address);
                                if (!string.IsNullOrEmpty(emailAddress.NameId) && !string.IsNullOrEmpty(emailAddress.Domain))
                                {
                                    var normalizedEmail = emailAddress.Normalize();
                                    userEmails.Add(normalizedEmail);
                                    s_logger.Debug($"Added validated email: {address.Address} -> {normalizedEmail} (native: {address.IsNative})");
                                }
                                else
                                {
                                    s_logger.Warn($"Skipped invalid email format: {address.Address}");
                                }
                            }
                            else
                            {
                                s_logger.Debug($"Skipped unvalidated email: {address.Address}");
                            }
                        }
                    }

                    lock (_cacheLock)
                    {
                        _cachedUserEmails = userEmails;
                        _lastCacheUpdate = DateTime.UtcNow;
                    }

                    s_logger.Info($"Successfully cached {userEmails.Count} validated user emails");
                    return userEmails;
                }
            }
            catch (HttpRequestException ex)
            {
                s_logger.Error($"HTTP error while fetching user emails: {ex.Message}", ex);
                throw new InvalidOperationException("Failed to fetch user emails from API", ex);
            }
            catch (JsonException ex)
            {
                s_logger.Error($"JSON parsing error while processing user emails response: {ex.Message}", ex);
                throw new InvalidOperationException("Invalid response format from user emails API", ex);
            }
            catch (Exception ex)
            {
                s_logger.Error($"Unexpected error while fetching user emails: {ex.Message}", ex);
                throw;
            }
        }

        public bool IsUserEmail(string emailToCheck)
        {
            if (string.IsNullOrEmpty(emailToCheck))
                return false;

            lock (_cacheLock)
            {
                if (!IsCacheValid())
                {
                    s_logger.Warn("No cached user emails available for comparison or cache is expired");
                    return false;
                }

                var normalizedEmailToCheck = NormalizeEmail(emailToCheck);
                var isUserEmail = _cachedUserEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmailToCheck);

                s_logger.Debug($"Email comparison: {emailToCheck} -> {isUserEmail}");
                return isUserEmail;
            }
        }

        public bool AreEmailsSame(string email1, string email2)
        {
            if (string.IsNullOrEmpty(email1) || string.IsNullOrEmpty(email2))
                return false;

            lock (_cacheLock)
            {
                if (!IsCacheValid())
                {
                    s_logger.Warn("No cached user emails available for comparison or cache is expired");
                    return false;
                }

                var normalizedEmail1 = NormalizeEmail(email1);
                var normalizedEmail2 = NormalizeEmail(email2);

                var email1IsUser = _cachedUserEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmail1);
                var email2IsUser = _cachedUserEmails.Any(email => NormalizeEmail(email.ToString()) == normalizedEmail2);

                if (email1IsUser && email2IsUser)
                {
                    s_logger.Debug($"Both emails belong to user: {email1} == {email2}");
                    return true;
                }

                var areSame = normalizedEmail1 == normalizedEmail2;
                s_logger.Debug($"Standard email comparison: {email1} == {email2} -> {areSame}");
                return areSame;
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedUserEmails = null;
                _lastCacheUpdate = DateTime.MinValue;
                s_logger.Info("User email cache cleared");
            }
        }


        private bool IsCacheValid()
        {
            return _cachedUserEmails != null &&
                   DateTime.UtcNow - _lastCacheUpdate < _cacheExpiration;
        }

        private string NormalizeEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return string.Empty;

            var parsed = EmailAddress.Parse(email.Trim());
            var normalized = parsed.Normalize();
            return $"{normalized.NameId.ToLowerInvariant()}@{normalized.Domain.ToLowerInvariant()}";
        }
    }
}
