using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using CalDavSynchronizer.DataAccess;
using CalDavSynchronizer.Ui.ConnectionTests;

namespace Y360OutlookConnector.Synchronization
{
    public class CalendarData
    {
        public Uri Uri { get; }
        public string Name { get; }
        public string CTag { get; }
        public AccessPrivileges Privileges { get; }

        public CalendarData(Uri uri, string name, AccessPrivileges privileges, string ctag)
        {
            Uri = uri;
            Name = name;
            CTag = ctag;
            Privileges = privileges;
        }
    }

    public class TaskListData
    {
        public string Id { get; }
        public string Name { get; }
        public string CTag { get; }
        public AccessPrivileges Privileges { get; }

        public TaskListData(string id, string name, AccessPrivileges privileges, string ctag)
        {
            Id = id;
            Name = name;
            Privileges = privileges;
            CTag = ctag;
        }
    }

    public class CalDavResources
    {
        public IReadOnlyList<CalendarData> CalendarResources { get; }
        public IReadOnlyList<TaskListData> TaskListResources { get; }

        public CalDavResources(IReadOnlyList<CalendarData> calendarResources, IReadOnlyList<TaskListData> taskTaskListResources)
        {
            CalendarResources = calendarResources ?? throw new ArgumentNullException(nameof(calendarResources));
            TaskListResources = taskTaskListResources ?? throw new ArgumentNullException(nameof(taskTaskListResources));
        }
    };


    public class CalDavResourcesDataAccess : WebDavDataAccess
    {
        public CalDavResourcesDataAccess(Uri serverUrl, IWebDavClient webDavClient)
            : base(serverUrl, webDavClient)
        {
        }

        private Task<XmlDocumentWithNamespaceManager> GetCalendarHomeSet(Uri url)
        {
            return _webDavClient.ExecuteWebDavRequestAndReadResponse(
                url,
                "PROPFIND",
                0,
                null,
                null,
                "application/xml",
                @"<?xml version='1.0'?>
                        <D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"">
                          <D:prop>
                            <C:calendar-home-set/>
                          </D:prop>
                        </D:propfind>
                 "
            );
        }

        public async Task<CalDavResources> GetResources()
        {
            var calendars = new List<CalendarData>();
            var taskLists = new List<TaskListData>();
            var currentUserPrincipalUrl = await GetCurrentUserPrincipalUrlOrNull(_serverUrl);
            if (currentUserPrincipalUrl != null)
            {
                var resources = await GetUserResources(currentUserPrincipalUrl);
                calendars.AddRange(resources.CalendarResources);
                taskLists.AddRange(resources.TaskListResources);
            }

            return new CalDavResources(calendars, taskLists);
        }

        private Task<XmlDocumentWithNamespaceManager> ListCalendars(Uri url)
        {
            return _webDavClient.ExecuteWebDavRequestAndReadResponse(
                url,
                "PROPFIND",
                1,
                null,
                null,
                "application/xml",
                @"<?xml version='1.0'?>
                        <D:propfind xmlns:D=""DAV:"" xmlns:C=""urn:ietf:params:xml:ns:caldav"" xmlns:E=""http://apple.com/ns/ical/"" xmlns:CS=""http://calendarserver.org/ns/"">
                          <D:prop>
                              <D:resourcetype />
                              <D:displayname />
                              <CS:getctag />
                              <E:calendar-color />
                              <C:supported-calendar-component-set />
                              <D:current-user-privilege-set />
                          </D:prop>
                        </D:propfind>
                 "
            );
        }


        private async Task<CalDavResources> GetUserResources(Uri principalUri)
        {
            var calendars = new List<CalendarData>();
            var taskLists = new List<TaskListData>();

            var calendarHomeSetProperties = await GetCalendarHomeSet(principalUri);

            XmlNode homeSetNode = calendarHomeSetProperties.XmlDocument.SelectSingleNode("/D:multistatus/D:response/D:propstat/D:prop/C:calendar-home-set", calendarHomeSetProperties.XmlNamespaceManager);
            if (homeSetNode != null && homeSetNode.HasChildNodes)
            {
                foreach (XmlNode homeSetNodeHref in homeSetNode.ChildNodes)
                {
                    if (!string.IsNullOrEmpty(homeSetNodeHref.InnerText))
                    {
                        var calendarHomeSetUri = Uri.IsWellFormedUriString(homeSetNodeHref.InnerText, UriKind.Absolute) ? new Uri(homeSetNodeHref.InnerText) : new Uri(calendarHomeSetProperties.DocumentUri.GetLeftPart(UriPartial.Authority) + homeSetNodeHref.InnerText);

                        var calendarDocument = await ListCalendars(calendarHomeSetUri);

                        var responseNodes = calendarDocument.XmlDocument.SelectNodes("/D:multistatus/D:response", calendarDocument.XmlNamespaceManager);

                        foreach (XmlElement responseElement in responseNodes)
                        {
                            var urlNode = responseElement.SelectSingleNode("D:href", calendarDocument.XmlNamespaceManager);
                            var displayNameNode = responseElement.SelectSingleNode("D:propstat/D:prop/D:displayname", calendarDocument.XmlNamespaceManager);
                            if (urlNode != null && displayNameNode != null)
                            {
                                var isCollection = responseElement.SelectSingleNode("D:propstat/D:prop/D:resourcetype/C:calendar", calendarDocument.XmlNamespaceManager);
                                if (isCollection != null)
                                {
                                    var supportedComponentsNode = responseElement.SelectSingleNode("D:propstat/D:prop/C:supported-calendar-component-set", calendarDocument.XmlNamespaceManager);
                                    if (supportedComponentsNode != null)
                                    {
                                        var ctag = ParseCTag(responseElement, calendarDocument.XmlNamespaceManager);
                                        var accessPrivileges = ParsePrivileges(responseElement, calendarDocument.XmlNamespaceManager);
                                        var path = urlNode.InnerText.EndsWith("/") ? urlNode.InnerText : urlNode.InnerText + "/";

                                        if (supportedComponentsNode.InnerXml.Contains("VEVENT"))
                                        {
                                            var displayName = string.IsNullOrEmpty(displayNameNode.InnerText) ? "Default Calendar" : displayNameNode.InnerText;
                                            var resourceUri = new Uri(calendarDocument.DocumentUri, path);
                                            calendars.Add(new CalendarData(resourceUri, displayName, accessPrivileges, ctag));
                                        }

                                        if (supportedComponentsNode.InnerXml.Contains("VTODO"))
                                        {
                                            var displayName = string.IsNullOrEmpty(displayNameNode.InnerText) ? "Default Tasks" : displayNameNode.InnerText;
                                            var resourceUri = new Uri(calendarDocument.DocumentUri, path);
                                            taskLists.Add(new TaskListData(resourceUri.ToString(), displayName, accessPrivileges, ctag));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return new CalDavResources(calendars, taskLists);
        }

        private static AccessPrivileges ParsePrivileges(XmlElement xmlElement,  XmlNamespaceManager nsManager)
        {
            XmlNode privilegeWriteContent = xmlElement.SelectSingleNode("D:propstat/D:prop/D:current-user-privilege-set/D:privilege/D:write-content", nsManager);
            XmlNode privilegeBind = xmlElement.SelectSingleNode("D:propstat/D:prop/D:current-user-privilege-set/D:privilege/D:bind", nsManager);
            XmlNode privilegeUnbind = xmlElement.SelectSingleNode("D:propstat/D:prop/D:current-user-privilege-set/D:privilege/D:unbind", nsManager);
            XmlNode privilegeWrite = xmlElement.SelectSingleNode("D:propstat/D:prop/D:current-user-privilege-set/D:privilege/D:write", nsManager);

            if (privilegeWrite != null)
                return AccessPrivileges.All;

            var privileges = AccessPrivileges.None;
            if (privilegeWriteContent != null) privileges |= AccessPrivileges.Modify;
            if (privilegeBind != null) privileges |= AccessPrivileges.Create;
            if (privilegeUnbind != null) privileges |= AccessPrivileges.Delete;
            return privileges;
        }

        private static string ParseCTag(XmlElement xmlElement,  XmlNamespaceManager nsManager)
        {
            XmlNode ctagNode = xmlElement.SelectSingleNode("D:propstat/D:prop/CS:getctag", nsManager);
            var ctag = ctagNode?.InnerText;
            return ctag ?? String.Empty;
        }
    }
}
