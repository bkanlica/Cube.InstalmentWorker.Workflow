using Cube.XRM.Framework.AddOn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cube.XRM.Framework;
using System.Activities;
using Microsoft.Xrm.Sdk.Workflow;
using Microsoft.Xrm.Sdk;
using System.Globalization;
using Microsoft.Crm.Sdk.Messages;

namespace Cube.InstalmentWorker.Workflow
{
    public class HistoryWatcher : WorkflowManager
    {
        private enum Types
        {
            PaymentBased = 0,
            Basic = 1,
            Detailed = 2
        }

        private enum Actions
        {
            CreateNewInstalments = 254450000,
            CancelAllInstalments = 254450001,
            ReCalculationRequired = 254450002,
            Completed = 254450003
        }

        protected override void Execute(CubeBase cubeBase)
        {
            cubeBase.LogSystem.CreateLog("HistoryWatcher is starting", System.Diagnostics.EventLogEntryType.Information);

            IWorkflowContext context = (IWorkflowContext)cubeBase.Context;
            cubeBase.LogSystem.CreateLog(context.PrimaryEntityId.ToString(), System.Diagnostics.EventLogEntryType.Information);
            Result result = cubeBase.RetrieveActions.getItemRetrieve(context.PrimaryEntityId, "mwns_instalmenthistory", null, true);

            if (!result.isError)
            {
                Entity history = (Entity)result.BusinessObject;
                if (history != null)
                {
                    cubeBase.LogSystem.CreateLog("Have history object", System.Diagnostics.EventLogEntryType.Information);

                    if (history.Contains("mwns_action"))
                    {
                        Actions actions = (Actions)((OptionSetValue)history["mwns_action"]).Value;
                        switch (actions)
                        {
                            case Actions.CreateNewInstalments:
                                Calculate(history, cubeBase);
                                break;
                            case Actions.CancelAllInstalments:
                                CancelInstalments(history, cubeBase);
                                break;
                            case Actions.ReCalculationRequired:
                                DeleteInstalments(history, cubeBase);
                                Calculate(history, cubeBase);
                                break;
                        }

                        //throw new Exception("exception!!!!");
                    }
                }
            }
        }

        private void Calculate(Entity history, CubeBase cubeBase)
        {
            IWorkflowContext context = (IWorkflowContext)cubeBase.Context;
            //calculation and creation
            if (history.Contains("mwns_type") && ((OptionSetValue)history["mwns_type"]).Value == (int)Types.Basic)
            {
                cubeBase.LogSystem.CreateLog("History Object type is: " + ((OptionSetValue)history["mwns_type"]).Value, System.Diagnostics.EventLogEntryType.Information);

                DateTime StartDate = ((DateTime)history["mwns_startdate"]);
                DateTime FinishDate = ((DateTime)history["mwns_finishdate"]);
                decimal Amount = ((Money)history["mwns_amount"]).Value;
                EntityReference Opportunity = ((EntityReference)history["mwns_opportunityid"]);
                var dateSpan = DateTimeSpan.CompareDates(StartDate, FinishDate);
                cubeBase.LogSystem.CreateLog("difference between dates : " + dateSpan.Months, System.Diagnostics.EventLogEntryType.Information);

                for (int i = 0; i < dateSpan.Months; i++)
                {
                    cubeBase.LogSystem.CreateLog("Filling installment object", System.Diagnostics.EventLogEntryType.Information);

                    Entity instalment = new Entity("mwns_instalment");
                    instalment.Attributes.Add("mwns_instalmenthistoryid", new EntityReference("mwns_instalmenthistory", context.PrimaryEntityId));
                    instalment.Attributes.Add("mwns_month", StartDate.AddMonths(i + 1).Month);
                    instalment.Attributes.Add("mwns_year", StartDate.AddMonths(i + 1).Year);
                    instalment.Attributes.Add("mwns_name", CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(StartDate.AddMonths(i + 1).Month));
                    instalment.Attributes.Add("mwns_value", new Money(Convert.ToDecimal(Amount / dateSpan.Months)));
                    instalment.Attributes.Add("mwns_opportunityid", Opportunity);
                    instalment.Attributes.Add("transactioncurrencyid", new EntityReference("transactioncurrency", new Guid("8F6BCCD4-5BD1-E411-80BD-00155D000A02")));

                    cubeBase.LogSystem.CreateLog("Creating installment object", System.Diagnostics.EventLogEntryType.Information);
                    Result createResult = cubeBase.XRMActions.Create(instalment);
                    if (createResult.isError)
                        throw new Exception(createResult.Message);

                    //throw new Exception("exception");
                }

                Update(cubeBase);
            }
        }

        private DataCollection<Entity> RetrieveAllInstalments(Entity history, CubeBase cubeBase)
        {
            cubeBase.LogSystem.CreateLog("RetrieveAllInstalments", System.Diagnostics.EventLogEntryType.Information);
            IWorkflowContext context = (IWorkflowContext)cubeBase.Context;
            EntityReference Opportunity = ((EntityReference)history["mwns_opportunityid"]);

            string fetch = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                              <entity name='mwns_instalment'>
                                <attribute name='mwns_instalmentid' />
                                <filter type='and'>
                                  <condition attribute='mwns_opportunityid' operator='eq' value='{0}' />
                                </filter>
                              </entity>
                            </fetch>";

            fetch = string.Format(fetch, Opportunity.Id);

            Result result = cubeBase.RetrieveActions.getItemsFetch(fetch);
            cubeBase.LogSystem.CreateLog(fetch, System.Diagnostics.EventLogEntryType.Information);
            if (!result.isError)
            {
                cubeBase.LogSystem.CreateLog("object[] returning...", System.Diagnostics.EventLogEntryType.Information);
                return (DataCollection<Entity>)result.BusinessObject;
            }
            else
                throw new Exception(result.Message);
        }

        private void DeleteInstalments(Entity history, CubeBase cubeBase)
        {
            cubeBase.LogSystem.CreateLog("DeleteInstalments", System.Diagnostics.EventLogEntryType.Information);
            DataCollection<Entity> result = RetrieveAllInstalments(history, cubeBase);
            if (result != null && result.Count > 0)
            {
                cubeBase.LogSystem.CreateLog("Have found instalments" + result.Count, System.Diagnostics.EventLogEntryType.Information);
                for (int i = 0; i < result.Count; i++)
                {
                    Result createResult = cubeBase.XRMActions.Delete(new Guid(((Entity)result[i])["mwns_instalmentid"].ToString()), "mwns_instalment");
                    cubeBase.LogSystem.CreateLog("deleted...", System.Diagnostics.EventLogEntryType.Information);
                    if (createResult.isError)
                        throw new Exception(createResult.Message);
                }
            }
        }

        private void CancelInstalments(Entity history, CubeBase cubeBase)
        {
            cubeBase.LogSystem.CreateLog("CancelInstalments", System.Diagnostics.EventLogEntryType.Information);
            DataCollection<Entity> result = RetrieveAllInstalments(history, cubeBase);
            if (result != null && result.Count > 0)
            {
                cubeBase.LogSystem.CreateLog("Have found instalments" + result.Count, System.Diagnostics.EventLogEntryType.Information);
                for (int i = 0; i < result.Count; i++)
                {
                    SetStateRequest request = new SetStateRequest()
                    {
                        EntityMoniker = new EntityReference("mwns_instalment") { Id = new Guid(((Entity)result[i])["mwns_instalmentid"].ToString()) },
                        State = new OptionSetValue(1),
                        Status = new OptionSetValue(1)
                    };

                    Result createResult = cubeBase.XRMActions.Execute<SetStateRequest, SetStateResponse>(request);
                    if (createResult.isError)
                        throw new Exception(createResult.Message);
                }
            }
        }

        private void Update(CubeBase cubeBase)
        {
            IWorkflowContext context = (IWorkflowContext)cubeBase.Context;
            Entity history = new Entity("mwns_instalmenthistory");
            history.Id = context.PrimaryEntityId;
            history.Attributes.Add("mwns_action", new OptionSetValue((int)Actions.Completed));
            Result updateResult = cubeBase.XRMActions.Update(history);
            if (updateResult.isError)
                throw new Exception(updateResult.Message);
        }

        public struct DateTimeSpan
        {
            private readonly int years;
            private readonly int months;
            private readonly int days;
            private readonly int hours;
            private readonly int minutes;
            private readonly int seconds;
            private readonly int milliseconds;

            public DateTimeSpan(int years, int months, int days, int hours, int minutes, int seconds, int milliseconds)
            {
                this.years = years;
                this.months = months;
                this.days = days;
                this.hours = hours;
                this.minutes = minutes;
                this.seconds = seconds;
                this.milliseconds = milliseconds;
            }

            public int Years { get { return years; } }
            public int Months { get { return months; } }
            public int Days { get { return days; } }
            public int Hours { get { return hours; } }
            public int Minutes { get { return minutes; } }
            public int Seconds { get { return seconds; } }
            public int Milliseconds { get { return milliseconds; } }

            enum Phase { Years, Months, Days, Done }

            public static DateTimeSpan CompareDates(DateTime date1, DateTime date2)
            {
                if (date2 < date1)
                {
                    var sub = date1;
                    date1 = date2;
                    date2 = sub;
                }

                DateTime current = date1;
                int years = 0;
                int months = 0;
                int days = 0;

                Phase phase = Phase.Years;
                DateTimeSpan span = new DateTimeSpan();

                while (phase != Phase.Done)
                {
                    switch (phase)
                    {
                        case Phase.Years:
                            if (current.AddYears(years + 1) > date2)
                            {
                                phase = Phase.Months;
                                current = current.AddYears(years);
                            }
                            else
                            {
                                years++;
                            }
                            break;
                        case Phase.Months:
                            if (current.AddMonths(months + 1) > date2)
                            {
                                phase = Phase.Days;
                                current = current.AddMonths(months);
                            }
                            else
                            {
                                months++;
                            }
                            break;
                        case Phase.Days:
                            if (current.AddDays(days + 1) > date2)
                            {
                                current = current.AddDays(days);
                                var timespan = date2 - current;
                                span = new DateTimeSpan(years, months, days, timespan.Hours, timespan.Minutes, timespan.Seconds, timespan.Milliseconds);
                                phase = Phase.Done;
                            }
                            else
                            {
                                days++;
                            }
                            break;
                    }
                }

                return span;
            }
        }
    }
}
