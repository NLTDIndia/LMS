using Hangfire;
using NLTD.EmployeePortal.LMS.Client;
using NLTD.EmployeePortal.LMS.Common.DisplayModel;
using NLTD.EmployeePortal.LMS.Common.QueryModel;
using NLTD.EmployeePortal.LMS.Repository;
using NLTD.EmployeePortal.LMS.Ux.AppHelpers;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace NLTD.EmployeePortal.LMS.Ux
{
    public class TimesheetEmailReportService
    {
        private string minDailyTime = ConfigurationManager.AppSettings["MinimumDailyTime"];
        private string minWeekTime = ConfigurationManager.AppSettings["MinimumWeekTime"];

        public void ProcessWeeklyReport()
        {
            DateTime today = DateTime.Now;
            while (today.DayOfWeek.ToString() != "Monday")
            {
                today = today.AddDays(-1);
            }
            GetTimesheetReportData(today.Date.AddDays(-7), today.Date.AddDays(-1));
            //GetTimesheetReportData(DateTime.Parse("4/3/2019", new CultureInfo("en-GB", true)), DateTime.Parse("10/3/2019", new CultureInfo("en-GB", true)));// Hard coded suresh
        }

        //public void ProcessMonthlyReport()
        //{
        //    DateTime lastMonth = DateTime.Now.Date.AddMonths(-1).AddDays(1);
        //    GetTimesheetReportData(lastMonth.Date, DateTime.Now.Date);
        //}

        public void GetTimesheetReportData(DateTime FromDate, DateTime ToDate)
        {
            TimeSheetQueryModel TimeSheetQueryModelObj = new TimeSheetQueryModel();
            ITimesheetHelper EmployeeAttendanceHelperObj = new TimesheetClient();
            IList<DailyTimeNotMaintainedModel> lstTimeSheetDailyNotMaintained = new List<DailyTimeNotMaintainedModel>();
            IList<WeeklyTimeNotMaintainedModel> lstTimeSheetWeeklyNotMaintained = new List<WeeklyTimeNotMaintainedModel>();
            string errorMessage = string.Empty;

            IList<long> lstUsers;
            try
            {
                TimeSheetQueryModelObj.FromDate = FromDate;
                TimeSheetQueryModelObj.ToDate = ToDate;
                TimeSheetQueryModelObj.UserID = EmployeeAttendanceHelperObj.GetHrUserId();
                TimeSheetQueryModelObj.MyDirectEmployees = false;
                List<TimeSheetModel> timeSheetModelList = EmployeeAttendanceHelperObj.GetMyTeamTimeSheet(TimeSheetQueryModelObj.UserID, TimeSheetQueryModelObj.FromDate, TimeSheetQueryModelObj.ToDate, TimeSheetQueryModelObj.MyDirectEmployees);
                timeSheetModelList = timeSheetModelList.OrderBy(x => x.userID).ToList();
                IList<WeeklyDateBlocksModel> lstWeeklyDates = BuildWeeklyDateBlocks(TimeSheetQueryModelObj.FromDate, TimeSheetQueryModelObj.ToDate);
                lstUsers = timeSheetModelList.Select(x => x.userID).Distinct().ToList();
                DailyTimeNotMaintainedModel dayMdl;
                foreach (var item in timeSheetModelList)
                {
                    if (!IsOneDayTimeMaintained(item))
                    {
                        dayMdl = new DailyTimeNotMaintainedModel
                        {
                            WorkingDay = item.WorkingDate,
                            WorkingDayText = item.WorkingDate.DayOfWeek.ToString(),
                            Shift = item.Shift,
                            InTime = item.InTime.Hour + ":" + item.InTime.ToString("mm") + ":" + item.InTime.ToString("ss"),
                            OutTime = item.OutTime.Hour + ":" + item.OutTime.ToString("mm") + ":" + item.OutTime.ToString("ss"),
                            TotalDayWorkingHoursFormatted = item.WorkingHours.ToString("hh") + ":" + item.WorkingHours.ToString("mm") + ":" + item.WorkingHours.ToString("ss"),
                            Status = item.Status,
                            Request = item.Requests,
                            UserId = item.userID
                        };
                        lstTimeSheetDailyNotMaintained.Add(dayMdl);
                    }
                }

                WeeklyTimeNotMaintainedModel weekMdl;
                TimeSpan totalWeekTime = TimeSpan.Zero;
                TimeSpan calculatedWeekTime = TimeSpan.Zero;
                decimal officialPermission = 0;
                decimal personalPermission = 0;
                decimal leaveDayQty = 0;
                decimal workFromHome = 0;
                bool isWeeklyMet = true;
                int minHours = (int.Parse)(minWeekTime.Split(':')[0]);
                int minMins = (int.Parse)(minWeekTime.Split(':')[1]);
                TimeSpan expectedWeekTime = new TimeSpan(minHours, minMins, 00);
                foreach (var item in lstUsers)
                {
                    foreach (var week in lstWeeklyDates)
                    {
                        totalWeekTime = TimeSpan.Zero;
                        calculatedWeekTime = TimeSpan.Zero;
                        officialPermission = 0;
                        personalPermission = 0;
                        leaveDayQty = 0;
                        workFromHome = 0;
                        isWeeklyMet = true;

                        CalculateTimeForAWeek(timeSheetModelList.Where(x => x.WorkingDate.Date >= week.WeekDayStartDate.Date && x.WorkingDate.Date <= week.WeekDayEndDate && x.userID == item).ToList(), out totalWeekTime, out calculatedWeekTime, out officialPermission, out personalPermission, out leaveDayQty, out workFromHome);

                        if (calculatedWeekTime.Ticks < expectedWeekTime.Ticks)
                        {
                            isWeeklyMet = false;
                        }
                        weekMdl = new WeeklyTimeNotMaintainedModel
                        {
                            DateRange = string.Format("{0} {1} - {2} {3}", week.WeekDayStartDate.Day, week.WeekDayStartDate.ToString("MMM"), week.WeekDayEndDate.Day, week.WeekDayEndDate.ToString("MMM")),
                            WorkFromHomeQty = workFromHome,
                            Permissions = "Permission(O: " + officialPermission + "hrs, " + "P: " + personalPermission + "hrs)",
                            TotalWeekWorkingHoursFormatted = ((totalWeekTime.Days) * 24 + totalWeekTime.Hours) + ":" + totalWeekTime.ToString("mm") + ":" + totalWeekTime.ToString("ss"),
                            Requests = leaveDayQty,
                            IsWeeklyTimeMet = isWeeklyMet,
                            UserId = item
                        };
                        lstTimeSheetWeeklyNotMaintained.Add(weekMdl);
                    }
                }

                //Get unique UserIds from both above lists
                string body = string.Empty;
                ITimesheetHelper timesheetClient = new TimesheetClient();
                IList<long> lstFinalUsers = lstTimeSheetDailyNotMaintained.Select(x => x.UserId).Distinct().ToList();
                IList<long> lstFinalWeekwiseUsers = lstTimeSheetWeeklyNotMaintained.Select(x => x.UserId).Distinct().ToList();
                lstFinalUsers = lstFinalUsers.Union(lstFinalWeekwiseUsers).ToList();

                IList<UserEmailListModel> lstUserEmail = timesheetClient.GetUserEmailData();
                List<string> ccEmailaddresses = new List<string>();
                foreach (var userId in lstFinalUsers)
                {
                    if (lstUserEmail.Where(p => p.UserId == userId).Count() > 0 && lstUserEmail.Where(p => p.UserId == userId && !String.IsNullOrEmpty(p.EmployeeEmailAddress)).Count() > 0 && lstUserEmail.FirstOrDefault(x => x.UserId == userId).SkipTimesheetCompliance == false)
                    {
                        ccEmailaddresses = new List<string>();
                        body = PrepareEmployeeEmailBody(lstTimeSheetDailyNotMaintained.Count > 0 ? lstTimeSheetDailyNotMaintained.Where(x => x.UserId == userId).ToList() : null, lstTimeSheetWeeklyNotMaintained.Count > 0 ? lstTimeSheetWeeklyNotMaintained.Where(x => x.UserId == userId).ToList() : null);
                        if (body != "NoEmail")
                        {
                            ccEmailaddresses.Add(lstUserEmail.Where(x => x.UserId == userId).FirstOrDefault().ReportingToEmailAddress);
                            SendEmail(body, lstUserEmail.Where(x => x.UserId == userId).FirstOrDefault().EmployeeEmailAddress, ccEmailaddresses, "LMS Timesheet Weekly Discrepancy Alert", lstUserEmail.Where(x => x.UserId == userId).FirstOrDefault().FirstName + " " + lstUserEmail.Where(x => x.UserId == userId).FirstOrDefault().LastName);
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public string PrepareEmployeeEmailBody(IList<DailyTimeNotMaintainedModel> lstDaywiseEmp, IList<WeeklyTimeNotMaintainedModel> lstEmpWeekwise)
        {
            if ((lstDaywiseEmp == null ? true : lstDaywiseEmp.Count == 0) && (lstEmpWeekwise == null ? true : lstEmpWeekwise.Count == 0))
            {
                return "NoEmail";
            }
            string body = string.Empty;

            foreach (var mdl in lstEmpWeekwise)
            {
                if ((lstDaywiseEmp == null ? false : lstDaywiseEmp.Count > 0) || mdl.IsWeeklyTimeMet == false)
                {
                    body += "<tr class='weeklytotal'><th>Summary</th><th>" + mdl.DateRange + "</th><th>Work From Home : " + mdl.WorkFromHomeQty + "</th><th colspan='2'>" + mdl.Permissions + "</th>" + (mdl.IsWeeklyTimeMet == true ? "<th>" : "<th style=\"color: Red\">") + mdl.TotalWeekWorkingHoursFormatted + "</th><th>" + "Leave : " + mdl.Requests + "</th></tr>";
                }
                if (lstDaywiseEmp == null ? false : lstDaywiseEmp.Count > 0)
                {
                    foreach (var dayMdl in lstDaywiseEmp)
                    {
                        body += "<tr><td>" + dayMdl.WorkingDay.ToString("dd-MM-yyyy") + "</td><td>" + dayMdl.WorkingDayText + "</td><td>" + dayMdl.Shift + "</td><td>" + dayMdl.InTime + "</td><td>" + dayMdl.OutTime + "</td><td style=\"color: Red\">" + dayMdl.TotalDayWorkingHoursFormatted + "</td><td>" + dayMdl.Request + "</td></tr>";
                    }
                }
            }
            if (body == "")
                return "NoEmail";
            else
                return body;
        }

        public void SendEmail(string body, string toEmailAddress, IList<string> lstCCEmailAddresses, string subject, string empName)
        {
            string emailBody = "<!DOCTYPE html><html><head><style>table{font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif; width: 100%; width: 100%; margin: 0 auto; clear: both; border-collapse: separate; border-spacing: 0; font-weight: normal; font-size: 12px;}tr.weeklytotal{background: #DFD5FD !important; font-weight: bold;}td, th{border: 1px solid #dddddd; text-align: left; padding: 8px;}tbody th, tbody td{padding: 8px 10px;}tbody > tr > td{vertical-align: top; border-top: 1px solid #ddd;}tr:nth-child(odd){background-color: #ffffffff;}tr:nth-child(even){background-color: #f9f9f9;}td.red{color: red;}td.blue{color: dodgerblue;}</style></head><body>"; ;
            emailBody += "<span font-family: 'Helvetica Neue', Helvetica, Arial, sans-serif;font-weight: normal; font-size: 12px;> Hello " + empName + ",<br/><br/>" + "Request you to fix the below timesheet discrepancy data highlighted in red color." + "<br/></span><br/><table> <tbody>";
            emailBody += "<tr> <th>Date</th> <th>Day</th> <th>Shift</th> <th>In Time</th> <th>Out Time</th> <th>Working Hours</th> <th>Requests</th> </tr>";
            emailBody += body;
            emailBody += "</tbody></table><br/><span>Thanks<br/>LMS<br/><br/>*** This is an automated message from LMS. Please do not reply to this email id. ***</span></body></html>";
            EmailHelper email = new EmailHelper();

#if DEBUG
            email.SendHtmlFormattedEmail(toEmailAddress, lstCCEmailAddresses, subject, emailBody);
#else
            BackgroundJob.Enqueue(() => email.SendHtmlFormattedEmail(toEmailAddress, lstCCEmailAddresses, subject, emailBody));
#endif
        }

        public IList<WeeklyDateBlocksModel> BuildWeeklyDateBlocks(DateTime fromDate, DateTime toDate)
        {
            //Monday to Sunday is 1 week block
            IList<WeeklyDateBlocksModel> lstWeeklyDates = new List<WeeklyDateBlocksModel>();
            DateTime currDate = fromDate;

            WeeklyDateBlocksModel mdl;
            while (currDate.Date <= toDate.Date)
            {
                if (currDate.DayOfWeek.ToString() == "Monday")
                {
                    mdl = new WeeklyDateBlocksModel
                    {
                        WeekDayStartDate = currDate.Date
                    };

                    if (currDate.Date.AddDays(6) <= toDate.Date)
                    {
                        mdl.WeekDayEndDate = currDate.AddDays(6);
                        lstWeeklyDates.Add(mdl);
                        currDate = currDate.AddDays(6);
                    }
                    else
                    {
                        break;
                    }
                }
                currDate = currDate.AddDays(1);
            }

            return lstWeeklyDates;
        }

        public bool IsOneDayTimeMaintained(TimeSheetModel timesheet)
        {
            if (timesheet.HolidayStatus == "Week Off")
            {
                return true;
            }

            TimeSpan calculatedWorkTime = CalculateTimeForADay(timesheet);
            int minHours = (int.Parse)(minDailyTime.Split(':')[0]);
            int minMins = (int.Parse)(minDailyTime.Split(':')[1]);
            TimeSpan expectedDailyTime = new TimeSpan(minHours, minMins, 00);

            if (calculatedWorkTime.Ticks >= expectedDailyTime.Ticks)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public TimeSpan CalculateTimeForADay(TimeSheetModel timesheet)
        {
            TimeSpan calculatedWorkTime = TimeSpan.Zero;
            calculatedWorkTime = timesheet.WorkingHours;
            if (timesheet.HolidayStatus == "Holiday")
            {
                calculatedWorkTime += new TimeSpan(9, 00, 00);
            }
            if ((double)timesheet.LeaveDayQty == 0.5)
            {
                calculatedWorkTime += new TimeSpan(4, 30, 00);
            }
            if (timesheet.LeaveDayQty == 1)
            {
                calculatedWorkTime += new TimeSpan(9, 00, 00);
            }
            if ((double)(timesheet.WorkFromHomeDayQty) == 0.5)
            {
                calculatedWorkTime += new TimeSpan(4, 30, 00);
            }
            else if ((double)(timesheet.WorkFromHomeDayQty) == 1)
            {
                calculatedWorkTime += new TimeSpan(9, 00, 00);
            }

            //Permission Hours
            if (timesheet.permissionCountPersonal > 0)
            {
                int minutes = (int)((timesheet.permissionCountPersonal) * 60);
                calculatedWorkTime += TimeSpan.FromMinutes(minutes);
            }
            if (timesheet.permissionCountOfficial > 0)
            {
                int minutes = (int)((timesheet.permissionCountOfficial) * 60);
                calculatedWorkTime += TimeSpan.FromMinutes(minutes);
            }

            return calculatedWorkTime;
        }

        public void CalculateTimeForAWeek(IList<TimeSheetModel> lstweekTimeSheet, out TimeSpan totalWeekHours, out TimeSpan calculatedWeekHours, out decimal officialPermission, out decimal personalPermission, out decimal leaveDayQty, out decimal workFromHome)
        {
            TimeSpan totalWeekTime = TimeSpan.Zero;
            calculatedWeekHours = TimeSpan.Zero;
            officialPermission = 0;
            personalPermission = 0;
            leaveDayQty = 0;
            workFromHome = 0;

            foreach (var day in lstweekTimeSheet)
            {
                if (day.Status != "Week Off")
                {
                    calculatedWeekHours += CalculateTimeForADay(day);
                    totalWeekTime = totalWeekTime.Add(new TimeSpan(0, day.WorkingHours.Hours, day.WorkingHours.Minutes, day.WorkingHours.Seconds, 0));
                    officialPermission += day.permissionCountOfficial;
                    personalPermission += day.permissionCountPersonal;
                    leaveDayQty += day.LeaveDayQty;
                    if (day.Requests.Contains("Work From Home"))
                    {
                        workFromHome += day.WorkFromHomeDayQty;
                    }
                }
            }
            totalWeekHours = totalWeekTime;
        }
    }
}