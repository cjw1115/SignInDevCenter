﻿using AngleSharp.Html.Parser;
using DevCenterGalley.Common.Models;
using DevCenterGallery.Common.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DevCenterGalley.Common.Services
{
    public class PersonalCookieService: BasicCookieService
    {
        private HttpService _httpService = new HttpService(false);

        public async override Task<string> GetDevCenterCookie()
        {
            var (username, password) = GetAccountInfo();
            Debug.Assert(!string.IsNullOrEmpty(username));
            Debug.Assert(!string.IsNullOrEmpty(password));

            var oAuthUrl = await _getOAuthUrlForDashboard();
            var authorizeConfig = await _getAuthorizeConfig(oAuthUrl);
            var urlGoToAADError = authorizeConfig.urlGoToAADError + $"&username={username}";
            var (urlPost, ppft) = await _getLiveLoginUrl(urlGoToAADError);
            (urlPost, ppft) = await _login(urlPost, username, password, ppft);
            var (code, state) = await _loginWithOpid(urlPost, ppft);
            var tokenDic = await _oAuth(code, state);
            var cookie =  await _authPostGateway(tokenDic);
            return cookie;
        }

        private async Task<string> _getOAuthUrlForDashboard()
        {
            var response = await _httpService.SendRequest("https://partner.microsoft.com/en-us/aad?action=signin&resource=797f4846-ba00-4fd7-ba43-dac1f8f63013&returnPath=%2fen-us%2fdashboard", HttpMethod.Get);
            if (response.StatusCode == System.Net.HttpStatusCode.Redirect)
            {
                var url = response.Headers.Location.ToString();
                return url.Replace("login.windows.net", "login.microsoftonline.com");
            }
            return null;
        }

        private class AuthorizeConfig
        {
            public string urlGoToAADError { get; set; }

        }
        private async Task<AuthorizeConfig> _getAuthorizeConfig(string authorizeUrl)
        {
            var response = await _httpService.SendRequest(authorizeUrl, HttpMethod.Get);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var strContent = await response.Content.ReadAsStringAsync();
                var startFlag = "<![CDATA[";
                var endFlag = "]]>";
                var startIndex = strContent.IndexOf(startFlag) + startFlag.Length;
                var endIndex = strContent.IndexOf(endFlag);
                strContent = strContent.Substring(startIndex, endIndex - startIndex);
                strContent = strContent.Replace("$Config=", "");
                strContent = strContent.Substring(0, strContent.LastIndexOf(';'));
                return JsonSerializer.Deserialize<AuthorizeConfig>(strContent);
            }
            return null;
        }

        private async Task<Tuple<string, string>> _getLiveLoginUrl(string url)
        {
            var response = await _httpService.SendRequest(url, HttpMethod.Get);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var strContent = await response.Content.ReadAsStringAsync();
                var startFlag = "urlPost:'";
                var startIndex = strContent.IndexOf(startFlag) + startFlag.Length;
                strContent = strContent.Substring(startIndex, strContent.Length - startIndex);
                var urlPost = strContent.Substring(0, strContent.IndexOf('\''));

                strContent = strContent.Substring(strContent.IndexOf("name=\"PPFT\""));
                startFlag = "value=\"";
                startIndex = strContent.IndexOf(startFlag) + startFlag.Length;
                var endIndex = strContent.IndexOf("\"/>");
                var ppft = strContent.Substring(startIndex, endIndex - startIndex);
                return Tuple.Create(urlPost, ppft);
            }
            return null;
        }

        private async Task<Tuple<string, string>> _login(string url, string username, string password, string ppft)
        {
            var contentDic = new Dictionary<string, string>
            {
                {"login",username },
                {"loginfmt",username },
                {"type","11" },
                {"LoginOptions","3" },
                {"passwd",password },
                {"ps","2" },
                {"PPFT",ppft },
                {"PPSX","Passp" },
                {"NewUser","1" },
                {"IsFidoSupported","1" }
            };
            FormUrlEncodedContent content = new FormUrlEncodedContent(contentDic);
            var response = await _httpService.SendRequest(url, HttpMethod.Post, content);
            
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var strContent = await response.Content.ReadAsStringAsync();
                var startFlag = "sFT:'";
                var startIndex = strContent.IndexOf(startFlag) + startFlag.Length;
                strContent = strContent.Substring(startIndex, strContent.Length - startIndex);
                var sFT = strContent.Substring(0, strContent.IndexOf('\''));

                startFlag = "urlPost:'";
                startIndex = strContent.IndexOf(startFlag) + startFlag.Length;
                strContent = strContent.Substring(startIndex, strContent.Length - startIndex);
                var urlPost = strContent.Substring(0, strContent.IndexOf('\''));

                return Tuple.Create(urlPost, ppft);
            }
            return null;
        }

        private async Task<Tuple<string, string>> _loginWithOpid(string url, string ppft)
        {
            var contentDic = new Dictionary<string, string>
            {
                {"type", "28" },
                {"LoginOptions", "3" },
                {"PPFT", ppft }
            };

            FormUrlEncodedContent content = new FormUrlEncodedContent(contentDic);
            var response = await _httpService.SendRequest(url, HttpMethod.Post, content);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var strContent = await response.Content.ReadAsStringAsync();

                HtmlParser parser = new HtmlParser();
                using (var doc = parser.ParseDocument(strContent))
                {
                    return Tuple.Create(
                        doc.GetElementsByName("code").FirstOrDefault()?.GetAttribute("value"),
                        doc.GetElementsByName("state").FirstOrDefault()?.GetAttribute("value"));
                }
            }
            return null;
        }

        private async Task<Dictionary<string, string>> _oAuth(string codeValue, string stateValue)
        {
            var url = "https://login.microsoftonline.com/common/federation/oauth2";
            var contentDic = new Dictionary<string, string>
            {
                {"code",codeValue },
                {"state",stateValue}
            };
            FormUrlEncodedContent content = new FormUrlEncodedContent(contentDic);
            var response = await _httpService.SendRequest(url, HttpMethod.Post, content);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var strContent = await response.Content.ReadAsStringAsync();
                HtmlParser parser = new HtmlParser();
                using (var doc = parser.ParseDocument(strContent))
                {
                    Dictionary<string, string> dic = new Dictionary<string, string>();
                    dic.Add("code", doc.GetElementsByName("code").FirstOrDefault()?.GetAttribute("value"));
                    dic.Add("id_token", doc.GetElementsByName("id_token").FirstOrDefault()?.GetAttribute("value"));
                    dic.Add("state", doc.GetElementsByName("state").FirstOrDefault()?.GetAttribute("value"));
                    dic.Add("session_state", doc.GetElementsByName("session_state").FirstOrDefault()?.GetAttribute("value"));
                    return dic;
                }
            }
            return null;
        }

        private async Task<string> _authPostGateway(Dictionary<string, string> dic)
        {
            var url = "https://partner.microsoft.com/aad/authPostGateway";
            FormUrlEncodedContent content = new FormUrlEncodedContent(dic);
            var response = await _httpService.SendRequest(url, HttpMethod.Post, content);
            if (response.StatusCode == System.Net.HttpStatusCode.Redirect)
            {
                return response.Headers.GetValues("Set-Cookie").Single(m => m.Contains(".AspNet.Cookies"));
            }
            return null;
        }

        class AccountInfoProxy
        {
            [JsonPropertyName("data")]
            public AccountInfo AccountInfo { get; set; }
        }

        private async Task<bool> _verifyOldCookie(string cookie)
        {
            var url = "https://partner.microsoft.com/api/partialui/delta";
            try
            {
                _httpService.SetCookie(url, cookie);
                var responseStr = await _httpService.SendRequestAndGetString(url, Encoding.UTF8, HttpMethod.Get);
                if (!string.IsNullOrEmpty(responseStr))
                {
                    var accountInfo = JsonSerializer.Deserialize<AccountInfoProxy>(responseStr).AccountInfo;
                    return accountInfo?.MeControl?.IsSignedIn ?? false;
                }
            }
            catch
            {
            }
            finally
            {
                _httpService.SetCookie(url, "");
            }
            return false;
        }
    }
}
