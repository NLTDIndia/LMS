﻿using Hangfire;
using NLTD.EmployeePortal.LMS.Client;
using NLTD.EmployeePortal.LMS.Common.DisplayModel;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Web.Hosting;

namespace NLTD.EmployeePortal.LMS.Ux.AppHelpers
{
    public class EmailHelper
    {
        private readonly string mailBaseUrlBody = ConfigurationManager.AppSettings["LMSUrl"];

        public void SendHtmlFormattedEmail(string recepientEmail, IList<string> ccEmail, string subject, string body)
        {
            try
            {
                string mailUserName = ConfigurationManager.AppSettings["UserName"];
                string mailHost = ConfigurationManager.AppSettings["Host"];
                bool mailEnableSsl = Convert.ToBoolean(ConfigurationManager.AppSettings["EnableSsl"]);
                string mailPassword = ConfigurationManager.AppSettings["Password"];
                int mailPort = int.Parse(ConfigurationManager.AppSettings["Port"]);

                using (MailMessage mailMessage = new MailMessage())
                {
                    mailMessage.From = new MailAddress(mailUserName);
                    mailMessage.Subject = subject;
                    mailMessage.Body = body;
                    mailMessage.IsBodyHtml = true;
                    mailMessage.To.Add(new MailAddress(recepientEmail));

                    foreach (var item in ccEmail)
                    {
                        mailMessage.CC.Add(item);
                    }

                    SmtpClient smtp = new SmtpClient
                    {
                        Host = mailHost,
                        EnableSsl = mailEnableSsl
                    };
                    System.Net.NetworkCredential NetworkCred = new System.Net.NetworkCredential
                    {
                        UserName = mailUserName,
                        Password = mailPassword
                    };
                    smtp.UseDefaultCredentials = true;
                    smtp.Credentials = NetworkCred;
                    smtp.Port = mailPort;
                    smtp.Send(mailMessage);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
            finally { }
        }

        private string PopulateBody(string userName, string description, string requestFor, string empId, string requestType, string range, string duration, string reason, string approverName, string approverComments, string actionName)
        {
            string body = string.Empty;

            if (actionName == "Pending")
            {
                using (var stream = new FileStream(HostingEnvironment.MapPath("~/EmailTemplate.html"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    StreamReader reader = new StreamReader(stream);
                    body = reader.ReadToEnd();
                }
            }
            else
            {
                using (var stream = new FileStream(HostingEnvironment.MapPath("~/EmailCCTemplate.html"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    StreamReader reader = new StreamReader(stream);
                    body = reader.ReadToEnd();
                }
            }

            body = body.Replace("{RequestFor}", requestFor);
            body = body.Replace("{EmpId}", empId);
            body = body.Replace("{RequestType}", requestType);
            body = body.Replace("{Range}", range);
            body = body.Replace("{Duration}", duration);
            body = body.Replace("{ApproverName}", approverName);
            body = body.Replace("{HelloUser}", " " + userName);
            body = body.Replace("{Description}", description);
            body = body.Replace("{Reason}", reason);
            body = body.Replace("{ApproverComments}", approverComments);
            body = body.Replace("{ManageLink}", mailBaseUrlBody + "/Leaves/ManageLeaveRequest");

            return body;
        }

        private string PopulateBodyforAddLeave(string userName, string description, string empId, string leaveType, string leaveBalance, string transaction, string days, string totalBalance, string remarks)
        {
            string body = string.Empty;

            using (var stream = new FileStream(HostingEnvironment.MapPath("~/EmailTemplateCreditLeave.html"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                StreamReader reader = new StreamReader(stream);
                body = reader.ReadToEnd();
            }

            body = body.Replace("{HelloUser}", " " + userName);
            body = body.Replace("{Description}", description);
            body = body.Replace("{RequestFor}", userName);
            body = body.Replace("{EmpId}", empId);
            body = body.Replace("{LeaveType}", leaveType);
            body = body.Replace("{LeaveBalance}", leaveBalance);
            body = body.Replace("{Transaction}", transaction);
            body = body.Replace("{Days}", days);
            body = body.Replace("{TotalBalance}", totalBalance);
            body = body.Replace("{Remarks}", remarks);

            return body;
        }

        public void SendRequestEmail(Int64 leaveId, string actionName)
        {
            try
            {
                EmailDataModel mdl;
                string helloUser = string.Empty;
                string description = string.Empty;

                using (var client = new LeaveClient())
                {
                    mdl = client.GetEmailData(leaveId, actionName);
                }
                if (mdl.ToEmailId == "AutoApproved")
                {
                    actionName = "Auto Approved";
                    mdl.ToEmailId = mdl.RequestorEmailId;
                }

                if (actionName == "Pending")
                {
                    helloUser = mdl.ReportingToName;
                    description = "The request of the below employee is pending for your action:";
                }
                else
                {
                    helloUser = mdl.RequestFor;
                    description = "Your request has been " + actionName + ".";
                }

                string body = string.Empty;

                body = this.PopulateBody(helloUser, description, mdl.RequestFor, mdl.EmpId, mdl.LeaveTypeText, mdl.Date, mdl.Duration, mdl.Reason, mdl.ReportingToName, mdl.ApproverComments, actionName);

                SendEmail(mdl.ToEmailId, mdl.CcEmailIds, "LMS - Request from " + mdl.RequestFor + " - " + actionName, body);
            }
            catch (Exception ex)
            {
                LogError(ex, leaveId);
                Elmah.ErrorLog.GetDefault(null).Log(new Elmah.Error(ex));
                throw;
            }
        }

        public void SendEmail(string recepientEmail, IList<string> ccEmail, string subject, string body)
        {
            try
            {
#if DEBUG
                SendHtmlFormattedEmail(recepientEmail, ccEmail, subject, body);
#else
                BackgroundJob.Enqueue(() => SendHtmlFormattedEmail(recepientEmail, ccEmail, subject, body));

#endif
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
            finally { }
        }

        public void SendEmailforAddLeave(List<EmployeeLeaveBalanceDetails> lst)
        {
            try
            {
                Int64 leaveTypeId = 0;
                if (lst.Count() > 0)
                {
                    for (int i = 0; i < lst.Count(); i++)
                    {
                        if (lst[i].NoOfDays > 0)
                        {
                            EmailDataModel mdl;
                            string description = string.Empty;
                            leaveTypeId = Convert.ToInt64(lst[i].LeaveTypeId);
                            using (var client = new LeaveClient())
                            {
                                mdl = client.GetEmailDataAddLeave(Convert.ToInt64(lst[i].UserId), leaveTypeId);
                            }

                            description = "Your " + mdl.LeaveTypeText + " balance has been updated.";

                            string body = string.Empty;
                            string transaction = lst[i].CreditOrDebit == "C" ? "Credit" : "Debit";
                            body = this.PopulateBodyforAddLeave(mdl.RequestFor, description, mdl.EmpId, mdl.LeaveTypeText, lst[i].BalanceDays.ToString(), transaction, lst[i].NoOfDays.ToString(), lst[i].TotalDays.ToString(), lst[i].Remarks);

                            SendEmail(mdl.RequestorEmailId, mdl.CcEmailIds, "LMS - " + mdl.RequestFor + " - " + mdl.LeaveTypeText + " balance updated", body);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null).Log(new Elmah.Error(ex));
                throw;
            }
        }

        private void LogError(Exception ex, Int64 leaveId = 0)
        {
            try
            {
                string message = string.Format("Time: {0}", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss tt"));
                message += Environment.NewLine;
                message += "Leave ID:" + leaveId;
                message += Environment.NewLine;
                message += "-----------------------------------------------------------";
                message += Environment.NewLine;
                message += string.Format("Message: {0}", ex.Message);
                message += Environment.NewLine;
                message += string.Format("StackTrace: {0}", ex.StackTrace);
                message += Environment.NewLine;
                message += string.Format("Source: {0}", ex.Source);
                message += Environment.NewLine;
                message += string.Format("TargetSite: {0}", ex.TargetSite.ToString());
                message += Environment.NewLine;
                message += "-----------------------------------------------------------";
                message += Environment.NewLine;
                string path = HostingEnvironment.MapPath("~/ErrorLog/ErrorLog.txt");
                using (StreamWriter writer = new StreamWriter(path, true))
                {
                    writer.WriteLine(message);
                }
            }
            catch { }
        }
    }
}