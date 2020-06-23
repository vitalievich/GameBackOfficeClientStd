using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Newtonsoft.Json;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GBOClientStd
{
    //public delegate void PartnerAddFreematch_dlg(FreeMatch fm);
    public delegate void PartnerSubsToFreematch_dlg(CompApplicant mpart);
    public delegate void PartnerUnSubsFromFreematch_dlg(string cid, string compid);
    public delegate void PartnerDelFreematch_dlg(string id);
    public delegate void NeighborConnChanged_dlg(string userid, string cid); // , bool state
    public delegate void ChatCountChanged_dlg(string userid, int mscount);

    public class Connector
    {
        //public event PartnerAddFreematch_dlg PartnerAddFreematch;
        public Action<CompApplicant> PartnerSubsFreeMatch;
        public Action<string,string> PartnerUnSubsFromFreematch;
        public Action<string> PartnerDelFreematch;
        public Action<string> OfferBuyed;
        
        public Action<string, string> NeighborConnChanged;
        public Action<string, int> ChatCountChanged;        

        public Action<List<Order>> GameActVolChanged;
        public Action<string, string, COMPTYPE, bool> PartnerChooseComp;
        public Action<CompApplicant, string, bool> PartnerEntersLeavesCompet;
        public Action<CompApplicant, string> PartnerSubscribeToTournir;
        public Action<List<CompApplicant>, string> PartnerSubscribeToChamp;
        public Action<string, string> PartnerUnSubscript;
        public Action<bool> ChampEnded;
        public Action<string, string> GamerChangedChampPlace;
        public Action<CompApplicant> PartnerStartComp;
        public Action<CompApplicant> FrameStarted;
        public Action<CompApplicant> TournRoundLeaved;
        public Action<NextRoundPlace> RoundEnded;
        public Action<CompSeededApps> CompSeeded;
        public Action<RoundStart> TournRoundStartSetted;
        public Action<Offer> OffersChanged;
        public Action<ChatMess> NewMessage;
        public Action ConnectClosed, Reconnected;
        public Action<int> Reconnecting;
        public Action<FreeMatch> PartnerAddFreematch;

        public string ConnectId => user == null ? "" : user.ConnectId;
        public string Id => user == null ? "" : user.Id;
        private static string OfficeUrl, DataUrl, LinkToPay;
        public UserLogin user;
        private readonly string _gameID;
        private static Connector connector = null;
        private Timer refreshTimer, datesynctimer; // 
        private HubConnection chatconnect;
        private Dictionary<string, Action<FreeMesssage>> OpenedMethods;
        private Regex EmailRx = new Regex(@"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
            @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private TimeSpan DateOffset = new TimeSpan(0);
        private static TimeSpan[] Delays = new TimeSpan[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(35) };
        public bool EnableCloseEvent = true;
        private static readonly HttpClient httpClient = new HttpClient();


        #region constructors
        private Connector(string gameID, string gboUrl)
        {
            LinkToPay = $"UserActives/BuyCurrency/?gid={gameID}";
            _gameID = gameID;
            OfficeUrl = gboUrl;
            DataUrl = $"{OfficeUrl}/Api/Data/";
            refreshTimer = new Timer(Refresh, null, -1, -1);
            datesynctimer = new Timer(SyncData, null, -1, -1);
            OpenedMethods = new Dictionary<string, Action<FreeMesssage>>();
            chatconnect = new HubConnectionBuilder()
            .WithUrl($"{OfficeUrl}/SR",  HttpTransportType.WebSockets, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    return await Task.FromResult(user.WsAccessToken);
                };
                //options.CloseTimeout = TimeSpan.FromSeconds(30);
                //options.WebSocketConfiguration = (wsc) =>
                //{
                //    wsc.SetBuffer(65536, 65536);
                //};

            })
            //.ConfigureLogging(logging =>
            //{
            //    logging.SetMinimumLevel(LogLevel.Trace);
            //    logging.AddDebug();
            //})
            .WithAutomaticReconnect(new RetryPolicy(Reconnect, Delays))
            .Build();
            chatconnect.Reconnected += async s =>
            {
                await Task.Run(() =>  RestartConnect());
                //System.Diagnostics.Debug.WriteLine($"TestWSConnect chatconnect.Reconnected  {DateTime.Now.ToString("mm:ss:ffff")}");
                //RefreshClient();
                //RebildServerHundlers();
                //await chatconnect.InvokeAsync("GameConnect");
                //Reconnected?.Invoke();
            };
            chatconnect.Closed += async e =>
            {
                if (EnableCloseEvent) ConnectClosed?.Invoke();
                await Task.CompletedTask;
            };
        }
        public bool IsConnected => chatconnect != null && chatconnect.State == HubConnectionState.Connected;
        public void RestartConnect()
        {
            System.Diagnostics.Debug.WriteLine($"TestWSConnect chatconnect.Reconnected  {DateTime.Now.ToString("mm:ss:ffff")}");
            RefreshClient();
            RebildServerHundlers();
            System.Diagnostics.Debug.WriteLine($"TestWSConnect 2 chatconnect.Reconnected  {DateTime.Now.ToString("mm:ss:ffff")}");
            chatconnect.StartAsync();
            chatconnect.InvokeAsync("GameConnect");
            System.Diagnostics.Debug.WriteLine($"TestWSConnect 3 chatconnect.Reconnected  {DateTime.Now.ToString("mm:ss:ffff")}");
            Reconnected?.Invoke();
        }
        public static Connector Instance(string gameID, string gboUrl)
        {
            if (connector == null)
            {
                connector = new Connector(gameID, gboUrl);
            }
            return connector;
        }
        public static Connector Instance(string gameID, string gboUrl, TimeSpan[] delays)
        {
            Delays = delays;
            return Instance(gameID, gboUrl);
        }
        public static Connector Instance() => connector;
        private void RebildServerHundlers()
        {
            chatconnect.Remove("UserChangeConnect");
            chatconnect.On<string, string>("UserChangeConnect", (uid, cid) =>
            {
                NeighborConnChanged?.Invoke(uid, cid);
            });

            chatconnect.Remove("CompSeeded");
            chatconnect.On<CompSeededApps>("CompSeeded", (tsa) =>
            {
                CompSeeded?.Invoke(tsa);
            });

            chatconnect.Remove("ParnterChooseComp");
            chatconnect.On<string, string, COMPTYPE>("ParnterChooseComp", (uid, cid, ctype) =>
                 PartnerChooseComp?.Invoke(uid, cid, ctype, true));

            chatconnect.Remove("ParnterOutOfChooseComp");
            chatconnect.On<string, string, COMPTYPE>("ParnterOutOfChooseComp", (uid, cid, ctype) =>
                  PartnerChooseComp?.Invoke(uid, cid, ctype, false));

            chatconnect.Remove("PartnerStartCompetition");
            chatconnect.On<CompApplicant>("PartnerStartCompetition", (participant) =>
                  PartnerStartComp?.Invoke(participant));

            chatconnect.Remove("FrameStarted");
            chatconnect.On<CompApplicant>("FrameStarted", (participant) =>
                  FrameStarted?.Invoke(participant));

            chatconnect.Remove("TournRoundLeaved");
            chatconnect.On<CompApplicant>("TournRoundLeaved", (participant) =>
                    TournRoundLeaved?.Invoke(participant));

            chatconnect.Remove("PartnerEntersLeavesCompet");
            chatconnect.On<CompApplicant, string, bool>("PartnerEntersLeavesCompet", (mp, cid, state) =>
            {
                System.Diagnostics.Debug.WriteLine($"TestWSConnect_ PartnerEntersLeavesCompet mp.CompId {mp.CompId} cid {cid}");
                PartnerEntersLeavesCompet?.Invoke(mp, cid, state);
            });

            chatconnect.Remove("PartnerSubscribeToTournir");
            chatconnect.On<CompApplicant, string>("PartnerSubscribeToTournir", (applicant, cid) =>
                    PartnerSubscribeToTournir?.Invoke(applicant, cid));

            chatconnect.Remove("PartnerSubscribeToChamp");
            chatconnect.On<List<CompApplicant>, string>("PartnerSubscribeToChamp", (apps, cid) =>
                    PartnerSubscribeToChamp?.Invoke(apps, cid));

            chatconnect.Remove("PartnerUnSubscribe");
            chatconnect.On<string, string>("PartnerUnSubscribe", (uid, cid) =>
                    PartnerUnSubscript?.Invoke(uid, cid));

            chatconnect.Remove("ChampEnded");
            chatconnect.On<bool>("ChampEnded", (b) =>
                     ChampEnded?.Invoke(b));

            chatconnect.Remove("GamerChangedChampPlace");
            chatconnect.On<string, string>("GamerChangedChampPlace", (s, b) =>
                    GamerChangedChampPlace?.Invoke(s, b));

            chatconnect.Remove("EndOfRound");
            chatconnect.On<NextRoundPlace>("EndOfRound", (next) =>
                    RoundEnded?.Invoke(next));

            chatconnect.Remove("TournRoundStartSetted");
            chatconnect.On<RoundStart>("TournRoundStartSetted", (start) =>
                   TournRoundStartSetted?.Invoke(start));

            chatconnect.Remove("NewMessage");
            chatconnect.On<ChatMess>("NewMessage", (s) =>
                    NewMessage?.Invoke(s));

            chatconnect.Remove("GameActVolChanged");
            chatconnect.On<List<Order>>("GameActVolChanged", (c) =>
                    GameActVolChanged?.Invoke(c));

            chatconnect.Remove("FreeMethod");
            chatconnect.On<string, FreeMesssage>("FreeMethod", (k, s) =>
                {
                    if (OpenedMethods.ContainsKey(k)) OpenedMethods[k]?.Invoke(s);
                });

            chatconnect.Remove("OffersChanged");
            chatconnect.On<Offer>("OffersChanged", (o) =>
                    OffersChanged?.Invoke(o));

            chatconnect.Remove("PartnerSubsFreeMatch");
            chatconnect.On<CompApplicant>("PartnerSubsFreeMatch", (mpart) =>
                    PartnerSubsFreeMatch?.Invoke(mpart));

            chatconnect.Remove("PartnerUnSubsFromFreematch");
            chatconnect.On<string, string>("PartnerUnSubsFromFreematch", (s, compid) =>
                    PartnerUnSubsFromFreematch?.Invoke(s, compid));

            chatconnect.Remove("ChatCountChanged");
            chatconnect.On<string, int>("ChatCountChanged", (s, n) =>
                    ChatCountChanged?.Invoke(s, n));

            chatconnect.Remove("PartnerAddFreematch");
            chatconnect.On<FreeMatch>("PartnerAddFreematch", (f) =>
            {
                System.Diagnostics.Debug.WriteLine($"TestWSConnect PartnerAddFreematch f.CompId {f.CompId} {DateTime.Now.ToString("mm:ss:ffff")}");
                PartnerAddFreematch?.Invoke(f);
            });
            chatconnect.Remove("OfferBuyed");
            chatconnect.On<string>("OfferBuyed", (i) =>
                    OfferBuyed?.Invoke(i));

            chatconnect.Remove("PartnerDelFreematch");
            chatconnect.On<string>("PartnerDelFreematch", (i) =>
            {
                System.Diagnostics.Debug.WriteLine($"TestWSConnect PartnerDelFreematch CompId {i} {DateTime.Now.ToString("mm:ss:ffff")}");
                PartnerDelFreematch?.Invoke(i);
            });
        }
        public static Uri UriToBuyCurrency(int volume) => new Uri($"{OfficeUrl}/{LinkToPay}&val={volume}");
        private void SyncData(object obj)
        {
            Task.Run(async () =>
            {
                var sd = await GetServerDateAsync();
                var ld = DateTime.Now;
                DateOffset = ld.Subtract(sd);
            });
            #region callback
            /*
            GetServerDateCallBack((sd) =>
            {
                var ld = DateTime.Now;
                DateOffset = ld.Subtract(sd);
            });
            */
            #endregion
        }
        //private void GetServerDateCallBack(Action<DateTime> action)
        //{
        //    chatconnect.On<DateTime>("Basedate", r =>
        //    {
        //        chatconnect.Remove("Basedate");
        //        action?.Invoke(r);
        //    });
        //    chatconnect.InvokeAsync("Basedate");
        //}
        private DateTime GetServerDate()
        {
            var rs = GetData($"basedate",false);
            DateTime res = JsonConvert.DeserializeObject<DateTime>(rs);
            return res;
        }
        private async Task<DateTime> GetServerDateAsync()
        {
            var rs = await GetDataAsync($"basedate",false);
            DateTime res = JsonConvert.DeserializeObject<DateTime>(rs);
            return res;
        }
        public DateTime SyncServerDate => DateTime.Now.Subtract(DateOffset);
        public void AddORChangeAction(string key, Action<FreeMesssage> action)
        {
            if (!OpenedMethods.ContainsKey(key)) OpenedMethods.Add(key, action);
            else OpenedMethods[key] = action;
        }
        public void DeleteAction(string key)
        {
            if (OpenedMethods.ContainsKey(key)) OpenedMethods.Remove(key);
        }
        #endregion

     
        #region connection
        public ISsfActionResult RegisterInGame(string username, string userpassword, string email, string phonenumber)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userpassword) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(_gameID))
                return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.WRONGARGUMENTS] };
            if (!EmailRx.IsMatch(email))
                return new SsfActionResult() { Error = ERROR.WRONGEMAILFORMAT, Message = ErrorMess.Messages[ERROR.WRONGEMAILFORMAT] };
            if (userpassword.Length < 6)
                return new SsfActionResult() { Error = ERROR.PASSWORDINVALID, Message = ErrorMess.Messages[ERROR.PASSWORDINVALID] };
            UserRegister usrReg = new UserRegister() { Name = username, Gameid = _gameID, Password = userpassword, Email = email, Grant_type = "register", PhoneNumber = phonenumber };

            string cnt;
            SsfActionResult actionResult;
            using (var client = new HttpClient())
            {
                using (var response = PostAsJson(client, $"{OfficeUrl}/register", usrReg))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound) return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                    cnt = response.Content.ReadAsStringAsync().Result;
                    if (!response.IsSuccessStatusCode)
                    {
                        return new SsfActionResult() { Error = ERROR.NETERROR, Message = cnt };
                    }
                }
            }
            actionResult = GetInnerError(cnt);
            return actionResult;
        }

        /// <summary>
        /// Стратегия авторизации и аутентификации:
        /// При первом входе по имени + е-майлу + паролю + ИД игры GBOClientStd.connector получает access_token и refresh_token 
        /// со сроком валидности 1 минута, в течении ее выполняет RefreshClient(),
        /// где получает новые access_token и  refresh_token со сроком валидности 1440 мин (1 сутки).
        /// 
        /// </summary>
        /// <param name="username"></param>
        /// <param name="userpassword"></param>
        /// <returns></returns>
        public SsfActionResult Login(string username, string userpassword)
        {
            user = new UserLogin() { Name = username, Password = userpassword, Gameid = _gameID, Grant_type = "password", TimeOffset = DateTimeOffset.Now.Offset.Hours };
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userpassword))
            {
                return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.WRONGARGUMENTS] };
            }
            string result = "";
            using (HttpClient client = new HttpClient())
            {
                using (HttpResponseMessage response = PostAsJson(client, $"{OfficeUrl}/Token", user))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                    }
                    result = response.Content.ReadAsStringAsync().Result;
                }
            }

            Dictionary<string, string> tokenDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);

            ERROR err = (ERROR)int.Parse(tokenDictionary["Error"]);
            if (err == ERROR.NOERROR)
            {
                user.RefreshToken = tokenDictionary["refresh_token"];
                user.AccessToken = tokenDictionary["access_token"];
                user.WsAccessToken = tokenDictionary["wsacess_token"];
                user.ConnectId = tokenDictionary["connectid"];
                user.Id = tokenDictionary["userid"];
                RefreshClient();
                InitChatConnect();
            }
            return new SsfActionResult() { Error = err, Message = ErrorMess.Messages[err] };
        }
        /// <summary>
        /// Обновление токенов:
        /// Первое обновление выполняется сразу после успешного логина.
        /// получает access_token и новый refresh_token со сроком валидности 1440 мин (1 сутки).
        /// Таймер обновления срабатывает через RefreshTimeOutMinute минут (от сервера) - 10 минут = 1430 мин.
        /// Клиентское приложение имеет возможность обновлять токены чаще.
        /// </summary>
        private SsfActionResult RefreshClient()
        {
            string result;
            ERROR err = ERROR.NOERROR;
            int RefreshTimeOutMsec = 86400000;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.RefreshToken);
            using (var response = PostAsJson(client, $"{OfficeUrl}/RefreshToken", null))
            {
                if (response.StatusCode != HttpStatusCode.OK) return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                result = response.Content.ReadAsStringAsync().Result;
            }
            Dictionary<string, string> tokenDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
            err = (ERROR)int.Parse(tokenDictionary["Error"]);

            if (err == ERROR.NOERROR)
            {
                RefreshTimeOutMsec = int.Parse(tokenDictionary["refreshtimeout"]);
                user.AccessToken = tokenDictionary["access_token"];
                user.RefreshToken = tokenDictionary["refresh_token"];
            }
            InitHttpClient();

            if (err == ERROR.NOERROR)
            {
                refreshTimer.Change(RefreshTimeOutMsec - 5000, RefreshTimeOutMsec);
                InitHttpClient();
            }
            return new SsfActionResult() { Error = err, Message = ErrorMess.Messages[err] };
        }
        private void InitChatConnect()
        {
            RebildServerHundlers();
            chatconnect.StartAsync();
            chatconnect.InvokeAsync("GameConnect");
        }
        private void Refresh(object state) => RefreshClient();

        private void Reconnect(int n)
        {
            Reconnecting?.Invoke(n);
        }

        public void AbortConnect()
        {
            chatconnect.InvokeAsync("AbortClient");
        }
        public void Exit()
        {
            try
            {
                if (datesynctimer != null)
                {
                    datesynctimer.Change(-1, -1);
                    datesynctimer.Dispose();
                }
                if (refreshTimer != null)
                {
                    refreshTimer.Change(-1, -1);
                    refreshTimer.Dispose();
                }
            }
            catch { }
            if (chatconnect != null)
            {
                if (chatconnect.State == HubConnectionState.Connected)
                     StopConnect();
                chatconnect.DisposeAsync();
            }
        }
        public void StopConnect()
        {
            if (chatconnect.State == HubConnectionState.Connected)
            {
                chatconnect.InvokeAsync("NetClientGoOut");
                chatconnect.StopAsync();
            }
        }
        #endregion
        #region CommonOperation
        private string GetData(string method, bool needAuth = true)
        {
            string getrequest = $"{DataUrl}{method}";
            //if (needAuth)
            //    InitHttpClient();
            var response = httpClient.GetAsync(getrequest).Result;
            if (!response.IsSuccessStatusCode)
            {
                return response.ReasonPhrase;
            }
            var cnt = response.Content.ReadAsStringAsync().Result;
            return cnt;
        }
        private async Task<string> GetDataAsync(string method, bool needAuth = true)
        {
            string getrequest = $"{DataUrl}{method}";
            var response = await httpClient.GetAsync(getrequest);
            if (!response.IsSuccessStatusCode)
            {
                return response.ReasonPhrase;
            }
            var cnt = await response.Content.ReadAsStringAsync();
            return cnt;
        }

        public void GetCurrencyNameCallBack(Action<string> action)
        {
            chatconnect.On<string>("GetCurrencyName", r =>
            {
                chatconnect.Remove("GetCurrencyName");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetCurrencyName");
        }
        public string GetCurrencyName()
        {
            return GetData("getcname");
        }
        public async Task<string> GetCurrencyNameAsync()
        {
            return await GetDataAsync("getcname");
        }

        public void GetGameParamsCallBack(Action<IEnumerable<CompParameter>> action)
        {
            chatconnect.On<IEnumerable<CompParameter>>("GetGameParams", r =>
            {
                chatconnect.Remove("GetGameParams");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetGameParams");
        }
        public IEnumerable<CompParameter> GetGameParams()
        {
            var p = GetData($"GetGameParams");
            var res = JsonConvert.DeserializeObject<IEnumerable<CompParameter>>(p);
            return res;
        }
        public async Task<IEnumerable<CompParameter>> GetGameParamsAsync()
        {
            var p = await GetDataAsync($"GetGameParams");
            var res = JsonConvert.DeserializeObject<IEnumerable<CompParameter>>(p);
            return res;
        }

        public void GetActiveDetailsCallBack(string activeid, DateTime start, DateTime end, Action<IEnumerable<UserInGameActiveMoving>> action)
        {
            string datereqstart = start.ToString("yyyyMMdd");
            string datereqend = end.ToString("yyyyMMdd");
            chatconnect.On<IEnumerable<UserInGameActiveMoving>>("GetActiveDetails", r =>
            {
                chatconnect.Remove("GetActiveDetails");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetActiveDetails", activeid, datereqstart, datereqend);
        }
        public IEnumerable<UserInGameActiveMoving> GetActiveDetails(string activeid, DateTime start, DateTime end)
        {
            string datereqstart = start.ToString("yyyyMMdd");
            string datereqend = end.ToString("yyyyMMdd");
            string res = GetData($"getactivedetails/{activeid}/{datereqstart}/{datereqend}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<UserInGameActiveMoving>>(res);
            return rss;
        }
        public async Task<IEnumerable<UserInGameActiveMoving>> GetActiveDetailsAsync(string activeid, DateTime start, DateTime end)
        {
            string datereqstart = start.ToString("yyyyMMdd");
            string datereqend = end.ToString("yyyyMMdd");
            string res = await GetDataAsync($"getactivedetails/{activeid}/{datereqstart}/{datereqend}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<UserInGameActiveMoving>>(res);
            return rss;
        }

        public void GetActivesCallBack(DateTime date, Action<List<GamerActive>> action)
        {
            chatconnect.On<List<GamerActive>>("GetActives", la =>
            {
                chatconnect.Remove("GetActives");
                action?.Invoke(la);
            });
            chatconnect.InvokeAsync("GetActives", date);
        }
   
        public async Task<List<GamerActive>> GetActivesAsync(DateTime date)
        {

            using (var responce = await PostAsJsonAsync($"{DataUrl}getactives", date))
            {
                if (!responce.IsSuccessStatusCode)
                    return new List<GamerActive>();
                var res = await responce.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<GamerActive>>(res);

                return result;
            }
        }
        public SsfActionResult GetActives(DateTime date, out List<GamerActive> result)
        {
            result = new List<GamerActive>();
            using (var responce = PostAsJson($"{DataUrl}getactives", date))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var res = responce.Content.ReadAsStringAsync().Result;
                result = JsonConvert.DeserializeObject<List<GamerActive>>(res);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };

        }     

        public void GetParamValueCallBack(string id, Action<int> action)
        {
            chatconnect.On<int>("GetParamValue", r =>
            {
                chatconnect.Remove("GetParamValue");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetParamValue", id);
        }
        public int GetParamValue(string id)
        {
            string res = GetData($"GetParamValue/{id}");
            var rss = JsonConvert.DeserializeObject<int>(res);
            return rss;
        }
        public async Task<int> GetParamValueAsync(string id)
        {
            string res = await GetDataAsync($"GetParamValue/{id}");
            var rss = JsonConvert.DeserializeObject<int>(res);
            return rss;
        }
        #endregion

        #region CommonCompetition
        public void SubscribeToTournirWebSock(string id) =>
            chatconnect.InvokeAsync("SubscribeToTournir", id);
        public void UnSubscribeFromCompetitionWebSock(string id) =>
            chatconnect.InvokeAsync("UnSubscribeFromCompetition", id);
        public void WriteFrameResultWebSock(FrameResult frameresults) =>
            chatconnect.InvokeAsync("WriteFrameResults", frameresults);
        public SsfActionResult WriteFrameResult(FrameResult frameresults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteFrameResults", frameresults))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public void WriteMatchWinnerWebSock(MatchResults matchResults) =>
            chatconnect.InvokeAsync("WriteMatchWinner", matchResults);
        public SsfActionResult WriteMatchWinner(string uid, MatchResults matchResults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteMatchWinner", new { uid, matchResults }))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        #endregion
        #region Tournir
        public void StartFrameWebSock(string placeid, string custopmparams) =>
            chatconnect.InvokeAsync("StartFrame", placeid, custopmparams);
        public void SetTournRoundStartWebSock(RoundStart start) =>
           chatconnect.InvokeAsync("SetTournRoundStart", start);
        public void EndOfRoundWebSock(string placeid, string winnerid) =>
            chatconnect.InvokeAsync("EndOfRound", placeid, winnerid);

        public void GetTournamentsCallBack(Action<List<Tournament>> action)
        {
            chatconnect.On<List<Tournament>>("Tournaments", r =>
            {
                chatconnect.Remove("Tournaments");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("Tournaments");
        }
        public List<Tournament> GetTournaments()
        {
            var p = GetData($"Tournaments");
            var res = JsonConvert.DeserializeObject<List<Tournament>>(p);
            return res;
        }
        public async Task<List<Tournament>> GetTournamentsAsync()
        {
            var p = await GetDataAsync($"Tournaments");
            var res = JsonConvert.DeserializeObject<List<Tournament>>(p);
            return res;
        }

        #endregion
        #region FreeMatches
        public void UnSubscribeFromFreeMatchWebSock(string cid) =>
            chatconnect.InvokeAsync("UnSubscribeFromFreeMatch", cid);
        public void SubscribeToFreeMatchWebSock(string cid) =>
            chatconnect.InvokeAsync("SubscribeToFreeMatch", cid);
        public void StartFreeMatchWebSock(string compid) =>
           chatconnect.InvokeAsync("StartFreeMatch", compid);
        public void DelFreeMatchWebSock(string compid) =>
            chatconnect.InvokeAsync("DelFreeMatch", compid);

        public void GetMatchResultsDetailCallBack(string matchid, Action<IEnumerable<FrameResult>> action)
        {
            chatconnect.On<IEnumerable<FrameResult>>("GetMatchResultsDetail", r =>
            {
                chatconnect.Remove("GetMatchResultsDetail");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetMatchResultsDetail", matchid);
        }
        public IEnumerable<FrameResult> GetMatchResultsDetail(string matchid)
        {
            string res = GetData($"GetMatchResultsDetail/{matchid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }
        public async Task<IEnumerable<FrameResult>> GetMatchResultsDetailAsync(string matchid)
        {
            string res = await GetDataAsync($"GetMatchResultsDetail/{matchid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }

        public void GetFreeMatchesCallBack(Action<List<FreeMatch>> action)
        {
            chatconnect.On<List<FreeMatch>>("FreeMatches", r =>
            {
                chatconnect.Remove("FreeMatches");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("FreeMatches");
        }
        public IEnumerable<FreeMatch> GetFreeMatches()
        {
            var p = GetData($"FreeMatches");
            var res = JsonConvert.DeserializeObject<IEnumerable<FreeMatch>>(p);
            return res;
        }
        public async Task<IEnumerable<FreeMatch>> GetFreeMatchesAsync()
        {
            var p = await GetDataAsync($"FreeMatches");
            var res = JsonConvert.DeserializeObject<IEnumerable<FreeMatch>>(p);
            return res;
        }

        #endregion
        #region Champs
        public void SubscribeToChampWebSock(string id) =>
            chatconnect.InvokeAsync("SubscribeToChamp", id);

        public void GetChampsCallBack(Action<List<Champ>> action)
        {
            chatconnect.On<List<Champ>>("Champs", r =>
            {
                chatconnect.Remove("Champs");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("Champs");
        }
        public List<Champ> GetChamps()
        {
            var p = GetData($"Champs");
            var res = JsonConvert.DeserializeObject<List<Champ>>(p);
            return res;
        }
        public async Task<List<Champ>> GetChampsAsync()
        {
            var p = await GetDataAsync($"Champs");
            var res = JsonConvert.DeserializeObject<List<Champ>>(p);
            return res;
        }

        public void StartChampPlaceWebSock(string placeid) =>
            chatconnect.InvokeAsync("StartChampPlace", placeid);
        public void WriteChampPlaceScoreWebSock(ChampPlaceScore champPlaceScore) =>
            chatconnect.InvokeAsync("WriteChampPlaceScore", champPlaceScore);
        public SsfActionResult WriteChampPlaceScore(ChampPlaceScore champPlaceScore)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteChampPlaceScore", champPlaceScore))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void WriteFreeMatchScoreWebSock(FreeMatchScore freeMatchScore) =>
            chatconnect.InvokeAsync("WriteFreeMatchScore", freeMatchScore);

        public SsfActionResult WriteFreeMatchScore(FreeMatchScore freeMatchScore)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteFreeMatchScore", freeMatchScore))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<SsfActionResult> WriteFreeMatchScoreAsync(FreeMatchScore freeMatchScore)
        {
            using (var responce = await PostAsJsonAsync(httpClient, $"{DataUrl}WriteFreeMatchScore", freeMatchScore))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }


        public void WriteChampFrameScoreWebSock(string userid, string pid) =>
            chatconnect.InvokeAsync("WriteChampFrameScore", userid, pid);
        public SsfActionResult WriteChampFrameScore(string userid, string pid)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteChampFrameScore", new string[] { userid, pid }))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void GetChampResultsDetailCallBack(string compid, Action<IEnumerable<FrameResult>> action)
        {
            chatconnect.On<IEnumerable<FrameResult>>("GetChampResultsDetail", r =>
            {
                chatconnect.Remove("GetChampResultsDetail");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetChampResultsDetail", compid);
        }
        public IEnumerable<FrameResult> GetChampResultsDetail(string compid)
        {
            string res = GetData($"GetChampResultsDetail/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }
        public async Task<IEnumerable<FrameResult>> GetChampResultsDetailAsync(string compid)
        {
            string res = await GetDataAsync($"GetChampResultsDetail/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }

        public IEnumerable<ChampMeetingScore> GetChampScore(string compid)
        {
            string res = GetData($"GetChampScore/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<ChampMeetingScore>>(res);
            return rss;
        }
        public async Task<IEnumerable<ChampMeetingScore>> GetChampScoreAsync(string compid)
        {
            string res = await GetDataAsync($"GetChampScore/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<ChampMeetingScore>>(res);
            return rss;
        }
        public SsfActionResult WriteChampPrefs(List<ChampPref> champresults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteChampPrefs", champresults))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        #endregion
        #region Anterior

        public void GetAnterResultsDetailCallBack(string compid, Action<IEnumerable<FrameResult>> action)
        {
            chatconnect.On<IEnumerable<FrameResult>>("GetAnterResultsDetail", r =>
            {
                chatconnect.Remove("GetAnterResultsDetail");
                action?.Invoke(r);
            });
            chatconnect.InvokeAsync("GetAnterResultsDetail", compid);
        }
        public IEnumerable<FrameResult> GetAnterResultsDetail(string compid)
        {
            string res = GetData($"GetAnterResultsDetail/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }
        public async Task<IEnumerable<FrameResult>> GetAnterResultsDetailAsync(string compid)
        {
            string res = await GetDataAsync($"GetAnterResultsDetail/{compid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<FrameResult>>(res);
            return rss;
        }

        public async Task<AnterApplicant> StartAnterMatchAsync(string compid)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}StartAnterMatch", compid))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var respdata = await responce.Content.ReadAsStringAsync();
                var anterApplicant = JsonConvert.DeserializeObject<AnterApplicant>(respdata);
                return anterApplicant;
            }
        }

        public void WriteAnterPrefsWebSock(List<AnterResult> champresults) =>
            chatconnect.InvokeAsync("WriteAnterPrefs", champresults);
        public SsfActionResult WriteAnterPrefs(List<AnterResult> champresults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteAnterPrefs", champresults))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        #endregion
        #region privatemethods
        private HttpResponseMessage PostAsJson(HttpClient client, string url, object obj)
        {
            try
            {
                if (obj == null)
                    return client.PostAsync(url, null).Result;
                var body = JsonConvert.SerializeObject(obj);
                return client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).Result;
            }
            catch 
            {                
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
        }
        private HttpResponseMessage PostAsJson(string url, object obj)
        {
            //InitHttpClient();
            try
            {
                if (obj == null)
                    return httpClient.PostAsync(url, null).Result;
                var body = JsonConvert.SerializeObject(obj);
                return httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).Result;
            }
            catch (AggregateException)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
        }
        private async Task<HttpResponseMessage> PostAsJsonAsync(string url, object obj)
        {
            //InitHttpClient();
            try
            {
                if (obj == null)
                    return await httpClient.PostAsync(url, null);
                var body = JsonConvert.SerializeObject(obj);
                var ttt = await httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                return ttt;
            }
            catch (AggregateException)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
        }
        private async Task<HttpResponseMessage> PostAsJsonAsync(HttpClient client, string url, object obj)
        {
            try
            {
                if (obj == null)
                    return await client.PostAsync(url, null);
                var body = JsonConvert.SerializeObject(obj);
                var ttt = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                return ttt;
            }
            catch (AggregateException)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
        }
        private SsfActionResult GetInnerError(string reqresult)
        {
            if (string.IsNullOrWhiteSpace(reqresult)) return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
            Dictionary<string, string> errorDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(reqresult);
            var lerr = (ERROR)int.Parse(errorDict["Error"]);
            SsfActionResult actionResult = new SsfActionResult() { Error = lerr, Message = ErrorMess.Messages[lerr] };
            return actionResult;
        }
        #endregion
        #region DataOperations    

        public void GetChatCallBack(string neghbid, Action<IEnumerable<ChatMess>> action)
        {
            chatconnect.On<IEnumerable<ChatMess>>("GetChatHist", res =>
            {
                chatconnect.Remove("GetChatHist");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetChatHist", neghbid);
        }
        public async Task<IEnumerable<ChatMess>> GetChatAsync(string neghbid)
        {
            var cnt = await GetDataAsync($"GetChatHist/{neghbid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<ChatMess>>(cnt);
            return rss;

        }
        public IEnumerable<ChatMess> GetChat(string neghbid)
        {
            string res = GetData($"GetChatHist/{neghbid}");
            var rss = JsonConvert.DeserializeObject<IEnumerable<ChatMess>>(res);
            return rss;
        }

        public void GetInterlocutorsCallBack(Action<IEnumerable<Interlocutor>> action)
        {
            chatconnect.On<IEnumerable<Interlocutor>>("GetInterlocutors", res =>
            {
                chatconnect.Remove("GetInterlocutors");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetInterlocutors");
        }
        public async Task<List<Interlocutor>> GetInterlocutorsAsync()
        {
            var p = await GetDataAsync($"GetInterlocutors");
            return JsonConvert.DeserializeObject<List<Interlocutor>>(p);
        }
        public List<Interlocutor> GetInterlocutors()
        {
            var p = GetData($"GetInterlocutors");
            return JsonConvert.DeserializeObject<List<Interlocutor>>(p);
        }

        public void GetAllChatCountCallBack(Action<int> action)
        {
            chatconnect.On<int>("GetAllChatCount", res =>
            {
                chatconnect.Remove("GetAllChatCount");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetAllChatCount");

        }
        public async Task<int> GetAllChatCountAsync()
        {
            var cnt = await GetDataAsync($"GetAllChatCount");
            return int.Parse(cnt);
        }
        public int GetAllChatCount()
        {
            var cnt = GetData($"GetAllChatCount");
            return int.Parse(cnt);
        }

        public void GetExchangeEntitiesCallBack(Action<List<ExchangeEntity>> action)
        {
            chatconnect.On<List<ExchangeEntity>>("GetExchangeEntities", res =>
            {
                chatconnect.Remove("GetExchangeEntities");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetExchangeEntities");
        }
        public async Task<List<ExchangeEntity>> GetExchangeEntitiesAsync()
        {
            string res = await GetDataAsync($"GetExchangeEntities");
            var rss = JsonConvert.DeserializeObject<List<ExchangeEntity>>(res);
            return rss;
        }
        public List<ExchangeEntity> GetExchangeEntities()
        {
            string res = GetData($"GetExchangeEntities");
            var rss = JsonConvert.DeserializeObject<List<ExchangeEntity>>(res);
            return rss;
        }

        public void GetOffersCallBack(Action<List<Offer>> action)
        {
            chatconnect.On<List<Offer>>("GetOffers", res =>
            {
                chatconnect.Remove("GetOffers");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetOffers");
        }
        public async Task<List<Offer>> GetOffersAsync()
        {
            var p = await GetDataAsync($"GetOffers");
            return JsonConvert.DeserializeObject<List<Offer>>(p);
        }
        public List<Offer> GetOffers()
        {
            var p = GetData($"GetOffers");
            return JsonConvert.DeserializeObject<List<Offer>>(p);
        }

        public void ChangeOfferCallBack(List<Good> ChangeOffer, Action<List<Good>> action)
        {
            chatconnect.On<List<Good>>("ChangeOffer", res =>
            {
                chatconnect.Remove("ChangeOffer");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("ChangeOffer", ChangeOffer);
        }
        public SsfActionResult ChangeOffer(List<Good> ChangeOffer, out ChangeOfferResult result)
        {
            result = new ChangeOfferResult() { ChangeState = -4 };
            using (var responce = PostAsJson(httpClient, $"{DataUrl}ChangeOffer", ChangeOffer))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var res = responce.Content.ReadAsStringAsync().Result;
                result = JsonConvert.DeserializeObject<ChangeOfferResult>(res);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<ChangeOfferResult> ChangeOfferAsync(List<Good> ChangeOffer)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}ChangeOffer", ChangeOffer))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new ChangeOfferResult() {ChangeState = -4 };
                }
                var res = await responce.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ChangeOfferResult>(res);
                return result;
            }
        }

        public void FindOffersCallBack(List<string> goodids, Action<List<Offer>> action)
        {
            chatconnect.On<List<Offer>>("FindOffers", res =>
            {
                chatconnect.Remove("FindOffers");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("FindOffers");
        }
        public async Task<List<Offer>> FindOffersAsync(List<string> goodids)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}FindOffers", goodids))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var res = await responce.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<Offer>>(res);
                return result;
            }
        }
        public List<Offer> FindOffers(List<string> goodids)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}FindOffers", goodids))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var res = (responce.Content.ReadAsStringAsync()).Result;
                var result = JsonConvert.DeserializeObject<List<Offer>>(res);
                return result;
            }
        }

        public void BuyOfferCallBack(string offid, Action<string> action)
        {
            chatconnect.On<string>("BuyOffer", res =>
            {
                chatconnect.Remove("BuyOffer");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("BuyOffer", offid);
        }
        public SsfActionResult BuyOffer(OfferState offerState, out OfferState result)
        {
            result = new OfferState();
            using (var responce = PostAsJson(httpClient, $"{DataUrl}BuyOffer", offerState))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var res = responce.Content.ReadAsStringAsync().Result;
                result = JsonConvert.DeserializeObject<OfferState>(res);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async  Task<OfferState> BuyOfferAsync(OfferState offerState)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}BuyOffer", offerState))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var result = await responce.Content.ReadAsStringAsync();
                var res = JsonConvert.DeserializeObject<OfferState>(result);
                return res;
            }
        }

        public void GetNewOfferActivesCallBack(string oid, Action<List<ActiveType>> action)
        {
            chatconnect.On<List<ActiveType>>("GetNewOfferActives", res =>
            {
                chatconnect.Remove("GetNewOfferActives");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetNewOfferActives", oid);
        }
        public async Task<List<ActiveType>> GetNewOfferActivesAsync(string oid)
        {
            var p = await GetDataAsync($"GetNewOfferActives/{oid}");
            var res = JsonConvert.DeserializeObject<List<ActiveType>>(p);
            return res;
        }
        public List<ActiveType> GetNewOfferActives(string oid)
        {
            var p = GetData($"GetNewOfferActives/{oid}");
            var res = JsonConvert.DeserializeObject<List<ActiveType>>(p);
            return res;
        }

        public void GetOfferContentCallBack(string oid, Action<List<Good>> action)
        {
            chatconnect.On<List<Good>>("OfferContent", res =>
            {
                chatconnect.Remove("OfferContent");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("OfferContent", oid, true);
        }
        public async Task<List<Good>> GetOfferContentAsync(string oid)
        {
            var p = await GetDataAsync($"OfferContent/{oid}/{true}");
            var res = JsonConvert.DeserializeObject<List<Good>>(p);
            return res;
        }
        public List<Good> GetOfferContent(string oid)
        {
            var p = GetData($"OfferContent/{oid}/{true}");
            var res = JsonConvert.DeserializeObject<List<Good>>(p);
            return res;
        }

        public void GetAnteriorsCallBack(Action<List<Anterior>> action)
        {
            chatconnect.On<List<Anterior>>("GetAnteriors", res =>
            {
                chatconnect.Remove("GetAnteriors");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetAnteriors");
        }
        public async Task<IEnumerable<Anterior>> GetAnteriorsAsync()
        {
            var p = await GetDataAsync($"GetAnteriors");
            var res = JsonConvert.DeserializeObject<List<Anterior>>(p);
            return res;
        }
        public IEnumerable<Anterior> GetAnteriors()
        {
            var p = GetData($"GetAnteriors");
            var res = JsonConvert.DeserializeObject<List<Anterior>>(p);
            return res;
        }

        public void GetTournirPlaceNeigborsCountCallBack(string placeid, Action<int> action)
        {
            chatconnect.On<int>("GetTournirPlaceNeigborsCount", res =>
            {
                chatconnect.Remove("GetTournirPlaceNeigborsCount");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("GetTournirPlaceNeigborsCount", placeid);
        }
        public async Task<int> GetTournirPlaceNeigborsCountAsync(string placeid)
        {
            var p = await GetDataAsync($"GetTournirPlaceNeigborsCount/{placeid}");
            var res = JsonConvert.DeserializeObject<int>(p);
            return res;
        }
        public int GetTournirPlaceNeigborsCount(string placeid)
        {
            var p = GetData($"GetTournirPlaceNeigborsCount/{placeid}");
            var res = JsonConvert.DeserializeObject<int>(p);
            return res;
        }

        public void SaveGetFrameFreeParamCallBack(FrameParam inframeParam, Action<FrameParam> action)
        {
            chatconnect.On<FrameParam>("SaveGetFrameFreeParam", res =>
            {
                chatconnect.Remove("SaveGetFrameFreeParam");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("SaveGetFrameFreeParam", inframeParam);
        }
        public SsfActionResult SaveGetFrameFreeParam(FrameParam inframeParam, out FrameParam outframeParam)
        {
            outframeParam = null;
            using (var responce = PostAsJson(httpClient, $"{DataUrl}SaveGetFrameFreeParam", inframeParam))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var result = responce.Content.ReadAsStringAsync().Result;
                outframeParam = JsonConvert.DeserializeObject<FrameParam>(result);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<FrameParam> SaveGetFrameFreeParamAsync(FrameParam inframeParam)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}SaveGetFrameFreeParam", inframeParam))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var result = await responce.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<FrameParam>(result);
            }
        }

        public void AddFreeMatchWebSock(FreeMatch freematch) =>
            chatconnect.InvokeAsync("NewFreeMatch", freematch);
        public async Task AddFreeMatchAsync(FreeMatch freematch)
        {
            await PostAsJsonAsync($"{DataUrl}NewFreeMatch", freematch);
        }

        public async Task AddOrderToActivityAsync(string Id, int volume, string comment, int iscurrency)
        {
           await PostAsJsonAsync($"{DataUrl}AddOrder", new Order() { Id = Id, Volume = volume, Comment = comment, IsCurrency = iscurrency });
        }
        public void AddOrderToActivityWebSock(string Id, int volume, string comment, int iscurrency)
        {
            chatconnect.InvokeAsync("AddOrder", new Order() { Id = Id, Volume = volume, Comment = comment, IsCurrency = iscurrency });
        }

        public void BuyActiveCallBack(string Id, int volume, Action<string> action)
        {
            chatconnect.On<string>("BuyActive", res =>
            {
                chatconnect.Remove("BuyActive");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("BuyActive", new Order() { Id = Id, Volume = volume });
        }
        public SsfActionResult BuyActive(string Id, int volume)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}BuyActive", new Order() { Id = Id, Volume = volume }))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                else
                {
                    var success = responce.Content.ReadAsStringAsync().Result;
                    if (!string.IsNullOrEmpty(success))
                    {
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = success };
                    }
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<SsfActionResult> BuyActiveAsync(string Id, int volume)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}BuyActive", new Order() { Id = Id, Volume = volume }))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                else
                {
                    var success = await responce.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(success))
                    {
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = success };
                    }
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void DoExchangeCallBack(ExchangeOrder exchangeOrder, Action<bool> action)
        {
            chatconnect.On<bool>("DoExchange", res =>
            {
                chatconnect.Remove("DoExchange");
                action?.Invoke(res);
            });
            chatconnect.InvokeAsync("DoExchange", exchangeOrder);
        }
        public async Task<SsfActionResult> DoExchangeAsync(ExchangeOrder exchangeOrder)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}DoExchange", exchangeOrder))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                else
                {
                    var success = await responce.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(success))
                    {
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = success };
                    }
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public SsfActionResult DoExchange(ExchangeOrder exchangeOrder)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}DoExchange", exchangeOrder))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                else
                {
                    var success = responce.Content.ReadAsStringAsync().Result;
                    if (!string.IsNullOrEmpty(success))
                    {
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = success };
                    }
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        #endregion

        #region Chat

        public void SetMessReadedWebSock(string messid) =>
            chatconnect.InvokeAsync("SetMessReaded", messid);
        public void SetMessReaded(string messid)
        {
            var responce = PostAsJson(httpClient, $"{DataUrl}SetMessReaded", messid);
        }
        public async Task SetMessReadedAsync(string messid)
        {
            var responce = await PostAsJsonAsync($"{DataUrl}SetMessReaded", messid);
        }
        public void ChooseCompetitionTypeWebSock(COMPTYPE comptype) =>
            chatconnect.InvokeAsync("EntersToCompType", comptype);
        public void LeaveCompTypeWebSock(COMPTYPE comptype) =>
            chatconnect.InvokeAsync("LeaveCompType", comptype);
        public void ChangeChampPlaceWebSock(string placeid) =>
            chatconnect.InvokeAsync("ChangeChampPlace", placeid);
        public void EnterToCompetitionWebSock(string cid)
        {
            chatconnect.InvokeAsync("EnterToCompetition", cid);
        }
        public void LeaveCompetitionWebSock(string cid)
        {
            chatconnect.InvokeAsync("LeaveCompetition", cid);
        }
        public void SendMessageWebSock(string recipientId, string text) =>
                chatconnect.InvokeAsync("TransMessageFC", recipientId, text);
        /// <summary>
        /// Произвольное сообщение другим клиентам из списка SsfConnectIds и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborMethodWebSock(string[] SsfConnectIds, bool needBack, string methodname, FreeMesssage methodparams) =>
                chatconnect.InvokeAsync("InvokeNeigborMethod", SsfConnectIds, needBack, methodname, methodparams);
        /// <summary>
        /// Произвольное сообщение партнерам по типу соревнования и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborsTheSameCompTypeWebSock(bool needBack, string methodname, FreeMesssage methodparams) =>
                chatconnect.InvokeAsync("InvokeNeigborsSCaS", needBack, methodname, methodparams);
        /// <summary>
        /// Произвольное сообщение партнерам по соревнованию и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborsTheSameCompWebSock(bool needBack, string methodname, FreeMesssage methodparams) =>
                chatconnect.InvokeAsync("InvokeNeigborsSTCaS", needBack, methodname, methodparams);
        #endregion
        private void InitHttpClient()
        {           
             httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        }

        internal class RetryPolicy : IRetryPolicy
        {
            private TimeSpan[] Delays;
            private readonly Action<int> Reconnecting;
            public RetryPolicy(Action<int> reconnecting, TimeSpan[] delays)
            {
                Delays = delays;
                Reconnecting = reconnecting;
            }
            public TimeSpan? NextRetryDelay(RetryContext retryContext)
            {
                System.Diagnostics.Debug.WriteLine($"TestWSConnect retryContext.PreviousRetryCount {retryContext.PreviousRetryCount} {DateTime.Now.ToString("mm:ss:ffff")}");
                if (retryContext.PreviousRetryCount > Delays.Length - 1) return null;
                Reconnecting?.Invoke((int)retryContext.PreviousRetryCount);
                return Delays[retryContext.PreviousRetryCount];
            }
        }
     
    }


}
