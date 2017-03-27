﻿using AzureB2BInvite.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static AzureB2BInvite.AdalUtil;

namespace AzureB2BInvite
{
    /// <summary>
    /// https://developer.microsoft.com/en-us/graph/docs/api-reference/beta/api/invitation_post
    /// </summary>
    public static class InviteManager
    {
        public static async Task<string> SendInvitation(GuestRequest request, PreAuthDomain domainSettings = null)
        {
            var displayName = string.Format("{0} {1}", request.FirstName, request.LastName);

            //use domain custom setting if exists, else use global site config setting
            var inviteRedirectUrl = (domainSettings == null) ?
                    Settings.DefaultRedirectUrl :
                        (!string.IsNullOrEmpty(domainSettings.RedirectLink))
                            ? domainSettings.RedirectLink
                                : Settings.DefaultRedirectUrl;

            var useCustomEmailTemplate = (domainSettings != null) & (!string.IsNullOrEmpty(domainSettings.InvitationTemplate));

            AdalResponse serverResponse = null;
            try
            {
                // Setup invitation
                var inviteEndPoint = string.Format("{0}/{1}/invitations", Settings.GraphResource, Settings.GraphApiVersion);
                GraphInvitation invitation = new GraphInvitation();
                invitation.InvitedUserDisplayName = displayName;
                invitation.InvitedUserEmailAddress = request.EmailAddress;
                invitation.InviteRedirectUrl = (!string.IsNullOrEmpty(inviteRedirectUrl)) ? inviteRedirectUrl : Settings.InviteRedirectUrl;
                invitation.SendInvitationMessage = (!Settings.UseSMTP);
                if (useCustomEmailTemplate)
                {
                    invitation.InvitedUserMessageInfo = new InvitedUserMessageInfo
                    {
                        CustomizedMessageBody=domainSettings.InvitationTemplate
                    };
                }

                // Invite user. Your app needs to have User.ReadWrite.All or Directory.ReadWrite.All to invite
                serverResponse = CallGraph(inviteEndPoint, invitation);
                var responseData = JsonConvert.DeserializeObject<GraphInvitation>(serverResponse.ResponseContent);
                if (responseData.id == null)
                {
                    var responseError = JsonConvert.DeserializeObject<ResponseError>(serverResponse.ResponseContent);
                    return string.Format("Invite not sent - API error: {0}", responseError.Code);
                }

                if (useCustomEmailTemplate)
                {
                    var emailSubject = Settings.InvitationEmailSubject.Replace("{{orgname}}", Settings.InvitingOrganization);

                    string body = FormatEmailBody(responseData);
                    SendViaSMTP(emailSubject, body, invitation.InvitedUserEmailAddress);
                }

                return responseData.Status;
            }
            catch (Exception ex)
            {
                var reason = (serverResponse == null ? "N/A" : serverResponse.ResponseContent);

                return string.Format("Error: {0}<br>Server response: {1}", ex.Message, reason);
            }
        }

        private static string FormatEmailBody(GraphInvitation data)
        {
            var body = new StringBuilder();
            body.AppendFormat("You've been invited to access applications in the {0} organization<br>", Settings.InvitingOrganization);
            body.AppendFormat("by {0}<br>", Settings.InviterResponseEmailAddr);
            body.AppendFormat("<a href='{0}'>Get Started</a><br>", data.InviteRedeemUrl);
            body.AppendFormat("Return to the above link at any time for access.<br><hr>");
            body.AppendFormat("Questions? Contact {0} at <a href='mailto:{1}'>{1}</a>", Settings.InvitingOrganization, Settings.InviterResponseEmailAddr);
            return body.ToString();
        }

        private static void SendViaSMTP(string subject, string mailBody, string email)
        {
            MailSender.SendMessage(email, subject, mailBody);
        }
        
        public static IEnumerable<GraphMemberRole> GetDirectoryRoles(string upn)
        {
            AdalResponse serverResponse = null;
            var rolesUri = string.Format("{0}/{1}/users/{2}/memberOf", Settings.GraphResource, Settings.GraphApiVersion, upn);
            serverResponse = CallGraph(rolesUri);
            var list = new List<GraphMemberRole>();
            if (serverResponse.Successful)
            {
                JObject res = JObject.Parse(serverResponse.ResponseContent);
                IList<JToken> roles = res["value"].ToList();
                foreach (var role in roles)
                {
                    var item = JsonConvert.DeserializeObject<GraphMemberRole>(role.ToString());
                    list.Add(item);
                }
            }

            return list;
        }
    }
}