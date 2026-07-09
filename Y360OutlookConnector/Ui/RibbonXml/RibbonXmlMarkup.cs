namespace Y360OutlookConnector.Ui.RibbonXml
{
    /// <summary>Сборка строк Custom UI для Explorer и окна встречи.</summary>
    internal static class RibbonXmlMarkup
    {
        internal static string BuildAppointmentCustomUi()
        {
            return
                "<customUI xmlns=\"http://schemas.microsoft.com/office/2009/07/customui\" onLoad=\"OnAppointmentRibbonLoad\">" +
                  "<ribbon>" +
                    "<tabs>" +
                      "<tab idMso=\"TabAppointment\">" +
                        "<group id=\"" + RibbonXmlIds.ApptGrpTele + "\">" +
                          "<menu id=\"" + RibbonXmlIds.ApptMenuTele + "\" size=\"large\" itemSize=\"normal\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\">" +
                            "<button id=\"" + RibbonXmlIds.ApptTeleInt + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.ApptTeleExt + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.ApptTeleSettings + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                          "</menu>" +
                        "</group>" +
                        "<group id=\"" + RibbonXmlIds.ApptGrpYc + "\">" +
                          "<menu id=\"" + RibbonXmlIds.ApptMenuYc + "\" size=\"large\" itemSize=\"normal\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\">" +
                            "<button id=\"" + RibbonXmlIds.ApptYcCreate + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.ApptYcEdit + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" getEnabled=\"GetAppointmentYandexEditEnabled\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.ApptYcNav + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                          "</menu>" +
                        "</group>" +
                      "</tab>" +
                      "<tab idMso=\"TabSchedulingAssistant\">" +
                        "<group id=\"" + RibbonXmlIds.SchedGrpYc + "\">" +
                          "<menu id=\"" + RibbonXmlIds.SchedMenuYc + "\" size=\"large\" itemSize=\"normal\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\">" +
                            "<button id=\"" + RibbonXmlIds.SchedYcCreate + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.SchedYcEdit + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" getEnabled=\"GetAppointmentYandexEditEnabled\" onAction=\"OnAppointmentRibbonAction\"/>" +
                            "<button id=\"" + RibbonXmlIds.SchedYcNav + "\" showImage=\"true\" getLabel=\"GetAppointmentRibbonLabel\" getImage=\"GetAppointmentRibbonControlImage\" onAction=\"OnAppointmentRibbonAction\"/>" +
                          "</menu>" +
                        "</group>" +
                      "</tab>" +
                    "</tabs>" +
                  "</ribbon>" +
                "</customUI>";
        }

        internal static string BuildExplorerCustomUi()
        {
            return
                "<customUI xmlns=\"http://schemas.microsoft.com/office/2009/07/customui\" " +
                "onLoad=\"OnExplorerRibbonLoad\">" +
                  "<ribbon>" +
                    "<tabs>" +
                      "<tab id=\"" + RibbonXmlIds.MainTab + "\" keytip=\"YC\" getLabel=\"GetExplorerRibbonLabel\" insertBeforeMso=\"TabHome\">" +
                        "<group id=\"" + RibbonXmlIds.MainGroup + "\" getLabel=\"GetExplorerRibbonLabel\">" +
                          "<button id=\"" + RibbonXmlIds.Login + "\" keytip=\"L\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" getVisible=\"GetExplorerRibbonControlVisible\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.SyncNow + "\" keytip=\"SN\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" getVisible=\"GetExplorerRibbonControlVisible\" " +
                                  "getEnabled=\"GetExplorerRibbonControlEnabled\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.SyncAll + "\" keytip=\"SA\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" getVisible=\"GetExplorerRibbonControlVisible\" " +
                                  "getEnabled=\"GetExplorerRibbonControlEnabled\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.Tools + "\" keytip=\"T\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" getVisible=\"GetExplorerRibbonControlVisible\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.Settings + "\" keytip=\"C\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.About + "\" keytip=\"AB\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "<button id=\"" + RibbonXmlIds.Help + "\" keytip=\"HP\" showImage=\"true\" " +
                                  "getLabel=\"GetExplorerRibbonLabel\" " +
                                  "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                        "</group>" +
                      "</tab>" +
                      "<tab idMso=\"TabCalendar\">" +
                        "<group id=\"" + RibbonXmlIds.HomeCreateGroup + "\" getLabel=\"GetExplorerRibbonLabel\" insertAfterMso=\"GroupCalendarNew\">" +
                          "<menu id=\"" + RibbonXmlIds.HomeYandexMenu + "\" size=\"large\" itemSize=\"normal\" showImage=\"true\" " +
                                "getLabel=\"GetExplorerRibbonLabel\" " +
                                "getImage=\"GetExplorerRibbonControlImage\">" +
                            "<button id=\"" + RibbonXmlIds.HomeYandexCreateItem + "\" showImage=\"true\" " +
                                    "getLabel=\"GetExplorerRibbonLabel\" " +
                                    "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                            "<button id=\"" + RibbonXmlIds.HomeYandexNavigateItem + "\" showImage=\"true\" " +
                                    "getLabel=\"GetExplorerRibbonLabel\" " +
                                    "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                          "</menu>" +
                        "</group>" +
                      "</tab>" +
                    "</tabs>" +
                  "</ribbon>" +
                  "<contextMenus>" +
                    "<contextMenu idMso=\"ContextMenuCalendarView\">" +
                      "<button id=\"" + RibbonXmlIds.ContextMenuCalendarViewButton + "\" " +
                              "getLabel=\"GetExplorerRibbonLabel\" showImage=\"true\" " +
                              "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                    "</contextMenu>" +
                    "<contextMenu idMso=\"MenuCalendarNewItem\">" +
                      "<button id=\"" + RibbonXmlIds.ContextMenuCalendarNewItem + "\" " +
                              "getLabel=\"GetExplorerRibbonLabel\" showImage=\"true\" " +
                              "onAction=\"OnExplorerRibbonAction\" getImage=\"GetExplorerRibbonControlImage\"/>" +
                    "</contextMenu>" +
                  "</contextMenus>" +
                "</customUI>";
        }
    }
}
