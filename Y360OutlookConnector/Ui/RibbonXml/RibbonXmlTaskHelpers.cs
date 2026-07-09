using System.Threading.Tasks;

namespace Y360OutlookConnector.Ui.RibbonXml
{
    internal static class RibbonXmlTaskHelpers
    {
        internal static void ObserveFaultedTask(Task task)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        ExceptionHandler.Instance.Unexpected(t.Exception.GetBaseException());
                    }
                },
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
