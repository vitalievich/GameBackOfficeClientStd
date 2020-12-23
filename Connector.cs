using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Newtonsoft.Json;
using System.Linq;
using System.Net.NetworkInformation;

namespace GBOClientStd
{
  
    public class Connector
    {
        public Action<CompApplicant> PartnerSubsFreeMatch;
        public Action<string, string> PartnerUnSubsFromFreematch;
        public Action<string> PartnerDelFreematch;
        public Action<string> OfferBuyed;
        public Action<AnterApplicant> AnterFrameStarted;

        public Action<string, string> NeighborConnChanged;
        public Action<string, int> ChatCountChanged;

        public Action<List<Order>> GameActVolChanged;
        public Action<string, string, COMPTYPE, bool> PartnerChooseComp;
        public Action<CompApplicant, string, bool> PartnerEntersLeavesCompet;
        public Action<CompApplicant, string> PartnerSubscribeToTournir;
        public Action<List<CompApplicant>, string> PartnerSubscribeToChamp;
        public Action<string, string> PartnerUnSubscribeFromComp;
        public Action<bool> ChampEnded;
        public Action<string, string> GamerChangedChampPlace;
        public Action<CompApplicant> PartnerStartComp;
        public Action<CompApplicant> GamerStartsFrame;
        public Action<CompApplicant> TournRoundLeaved;
        public Action<NextRoundPlace> RoundEnded;
        public Action<CompSeededApps> CompSeeded;
        public Action<MatchResults> FreeMatchEnded;
        public Action<RoundStart> TournRoundStartSetted;
        public Action<ExchangeOffer> OffersChanged;
        public Action<Chat> NewMessage;
        public Action ConnectClosed, Reconnected, BeforeExecNestedAction;
        
        public Action Reconnecting;
        public Action<FreeMatch> PartnerAddFreematch;
        public Action FinalDisconnect;
        public Action<string,UserInCompState> GamerEndsFrame;
        public Action<List<StringAndInt>> FrameEnded;
        private readonly Action<string> UpdateAccessToken;

        public string ConnectId => user == null ? "" : user.ConnectId;
        public string Id => user == null ? "" : user.Id;
        private static string OfficeUrl, DataUrl, LinkToPay, GBOAddress;
        public UserLogin user;
        private readonly string _gameID;
        private static Connector connector = null;
        private Timer refreshTimer, datesynctimer; // 
        private HubConnection chatconnect;
        private Dictionary<string, Action<FreeMesssage>> OpenedMethods;
        private Regex EmailRx = new Regex(@"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
            @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-\w]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private TimeSpan DateOffset = new TimeSpan(0);
        public bool EnableCloseEvent = true;
        private static readonly HttpClient httpClient = new HttpClient();

        public static ConcurrentDictionary<int, Action> NestedActions = new ConcurrentDictionary<int, Action>();

        private static int ErrorPingAttempt = 0, SuccessPingAttempt = 0;
        private const int MAXPINGATTEMPT = 15, ENOUGHPINGATTEMPT = 5; 
        private Ping Pinger;

        #region constructors
        private Connector(string gameID, string gboUrl, Action<string> action)
        {
            LinkToPay = $"UserActives/BuyCurrency/?gid={gameID}";
            _gameID = gameID;
            UpdateAccessToken = action;
            OfficeUrl = gboUrl;
            GBOAddress = gboUrl.Substring(gboUrl.IndexOf("//") + 2);
            DataUrl = $"{OfficeUrl}/Api/Data/";
            refreshTimer = new Timer(Refresh, null, -1, -1);
            datesynctimer = new Timer(SyncDate, null, -1, -1);
            Pinger = new Ping();
           
            OpenedMethods = new Dictionary<string, Action<FreeMesssage>>();
            InitSignalR();
        }
        public static Connector Instance(string gameID, string gboUrl, Action<string> updateAccessToken)
        {
            if (connector == null)
            {
                connector = new Connector(gameID, gboUrl, updateAccessToken);
            }
            return connector;
        }
        public static Connector Instance() => connector;
        #endregion

        #region connection

        /// <summary>
        /// Проверка доступности сервера
        /// </summary>
        /// <param name="serverurl"></param>
        /// <returns>bool</returns>
        public async  static void IsServerReady(string serverurl, Action<bool> ServerReady)
        {
            //    try
            //    {
            var response = new HttpClient().GetAsync($"{serverurl}/Api/Data/CheckConnect").Result; //  {Timeout = TimeSpan.FromSeconds(5) }
                ServerReady?.Invoke(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized);
            //}
            //catch (Exception ex)
            //{
            //    ServerReady?.Invoke(false);
            //}
            await Task.CompletedTask;
        }
        private void InitSignalR()
        {
            chatconnect = new HubConnectionBuilder()
            .WithUrl($"{OfficeUrl}/SR", HttpTransportType.WebSockets, options =>
            {
                options.AccessTokenProvider = async () =>  await Task.FromResult(user.WsAccessToken);
                options.SkipNegotiation = true;
            })
            .Build();
            chatconnect.Reconnected += async s => await RestartConnect();
            chatconnect.Closed += async e =>
            {
                await Task.CompletedTask;
                Reconnecting?.Invoke();
                PingServer();
            };
        }
        /// <summary>
        /// Метод стартует при потери связи (событие chatconnect.Closed).
        /// 15 неудачных пингов (~ 76 сек) означают невосстановимую потерю сети,
        /// очищается хранилище отложенных команд NestedActions и вызывается Action FinalDisconnect,
        /// информирующий приложение об окончательной потере связи.
        /// Иначе, после 10 успешных пингов вызывается метод RestartConnect
        /// </summary>
        private void PingServer()
        {
            while (true)
            {
                    var pingReply = Pinger.Send(GBOAddress);
                    if (pingReply.Status != IPStatus.Success)
                    {
                        ErrorPingAttempt += 1;
                        SuccessPingAttempt = 0;
                        if (ErrorPingAttempt >= MAXPINGATTEMPT)
                        {
                            ErrorPingAttempt = 0;
                            NestedActions.Clear();
                            FinalDisconnect?.Invoke();
                            break;
                        }
                    }
                    else
                    {
                        SuccessPingAttempt += 1;
                        ErrorPingAttempt = 0;
                        if (SuccessPingAttempt >= ENOUGHPINGATTEMPT)
                        {
                            SuccessPingAttempt = 0;
                            Task.Run(async () => await RestartConnect());
                            break;
                        }
                    }
             
            }
        }
        /// <summary>
        /// Проверяется доступность сервиса (CheckConnect), авторизуемый по websocket
        /// Если связь восстановилась быстрее чем за ~ 1 мин (серверная настройка) и CheckConnect вернул код 200
        /// повторный рефреш / логин не требуется.     
        /// </summary>
        /// <returns></returns>
        public async Task RestartConnect()
        {
            if (chatconnect == null)
                throw new InvalidOperationException("Коннектор не инициализирован.");
            if (chatconnect.State == HubConnectionState.Connected)
            return;
            try
            {
                var response = httpClient.GetAsync($"{OfficeUrl}/Api/Data/CheckConnect").Result;
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var att = RefreshClient();
                    if (att.Error != ERROR.NOERROR)
                        LoginByToken(user);
                }
            }
            catch
            {
                FinalDisconnect?.Invoke(); 
                return;
            }
            await chatconnect.StopAsync();
            InitChatConnect();

            BeforeExecNestedAction?.Invoke();
            foreach (var key in NestedActions.Keys.OrderBy(x => x))
            {
                if (NestedActions.TryRemove(key, out var action))
                    action?.Invoke();
            }
            Reconnected?.Invoke();
        }
        public void StopConnect()
        {
            if (chatconnect == null)
                throw new InvalidOperationException("Коннектор не инициализирован.");
            if (chatconnect.State != HubConnectionState.Connected)
                return;
            chatconnect.InvokeAsync("GameDisconnect");
            chatconnect.StopAsync();
        }
        public bool IsConnected => chatconnect != null && chatconnect.State == HubConnectionState.Connected;
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

            chatconnect.Remove("FreeMatchEnded");
            chatconnect.On<MatchResults>("FreeMatchEnded", (result) =>
                FreeMatchEnded?.Invoke(result));

            chatconnect.Remove("ParnterChooseComp");
            chatconnect.On<string, string, COMPTYPE>("ParnterChooseComp", (uid, cid, ctype) =>
                 PartnerChooseComp?.Invoke(uid, cid, ctype, true));

            chatconnect.Remove("ParnterOutOfChooseComp");
            chatconnect.On<string, string, COMPTYPE>("ParnterOutOfChooseComp", (uid, cid, ctype) =>
                  PartnerChooseComp?.Invoke(uid, cid, ctype, false));

            chatconnect.Remove("PartnerStartCompetition");
            chatconnect.On<CompApplicant>("PartnerStartCompetition", (participant) =>
                  PartnerStartComp?.Invoke(participant));

            chatconnect.Remove("GamerStartsFrame");
            chatconnect.On<CompApplicant>("GamerStartsFrame", (participant) =>
                  GamerStartsFrame?.Invoke(participant));

            chatconnect.Remove("TournRoundLeaved");
            chatconnect.On<CompApplicant>("TournRoundLeaved", (participant) =>
                    TournRoundLeaved?.Invoke(participant));

            chatconnect.Remove("PartnerEntersLeavesCompet");
            chatconnect.On<CompApplicant, string, bool>("PartnerEntersLeavesCompet", (mp, cid, state) =>
            {
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
                    PartnerUnSubscribeFromComp?.Invoke(uid, cid));

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
            chatconnect.On<Chat>("NewMessage", (s) =>
                    NewMessage?.Invoke(s));

            chatconnect.Remove("GameActVolChanged");
            chatconnect.On<List<Order>>("GameActVolChanged", (c) =>
                    GameActVolChanged?.Invoke(c));

            chatconnect.Remove("GamerEndsFrame");
            chatconnect.On<string, UserInCompState>("GamerEndsFrame", (userid, state) =>  GamerEndsFrame?.Invoke(userid, state));

            chatconnect.Remove("FrameEnded");
            chatconnect.On<List<StringAndInt>>("FrameEnded", (scores) => FrameEnded?.Invoke(scores));

            chatconnect.Remove("FreeMethod");
            chatconnect.On<string, FreeMesssage>("FreeMethod", (k, s) =>
            {
                if (OpenedMethods.ContainsKey(k))
                    OpenedMethods[k]?.Invoke(s);
            });

            chatconnect.Remove("OffersChanged");
            chatconnect.On<ExchangeOffer>("OffersChanged", (o) => OffersChanged?.Invoke(o));

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
                PartnerAddFreematch?.Invoke(f);
            });
            chatconnect.Remove("OfferBuyed");
            chatconnect.On<string>("OfferBuyed", (i) =>
                    OfferBuyed?.Invoke(i));

            chatconnect.Remove("PartnerDelFreematch");
            chatconnect.On<string>("PartnerDelFreematch", (i) =>
            {
                PartnerDelFreematch?.Invoke(i);
            });
            chatconnect.Remove("AnterFrameStarted");
            chatconnect.On<AnterApplicant>("AnterFrameStarted", (x) =>
                    AnterFrameStarted?.Invoke(x));
        }
        public SsfActionResult RegisterInGame(string username, string userpassword, string email, string phonenumber)
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
        public SsfActionResult LoginByToken(UserLogin userLogin)
        {
            string result = "";
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userLogin.AccessToken);
                using (HttpResponseMessage response = PostAsJson(client, $"{OfficeUrl}/TryLogin", userLogin.DeviceId))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                    }
                    result = response.Content.ReadAsStringAsync().Result;
                }
            }

            Dictionary<string, string> tokenDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);

            if (!tokenDictionary.ContainsKey("connectid") || string.IsNullOrEmpty(tokenDictionary["connectid"]))
                throw new Exception("Error connectid");

            ERROR err = (ERROR)int.Parse(tokenDictionary["Error"]);
            int RefreshTimeOutMsec = 86400000;

            if (err == ERROR.NOERROR)
            {
                RefreshTimeOutMsec = int.Parse(tokenDictionary["refreshtimeout"]);

                user = new UserLogin() { Name = userLogin.Name
                    , Password = userLogin.Password
                    , Gameid = _gameID
                    , Grant_type = "password"
                    , TimeOffset = DateTimeOffset.Now.Offset.Hours
                    , DeviceId = userLogin.DeviceId };

                user.WsAccessToken = tokenDictionary["wsacess_token"];
                user.AccessToken = tokenDictionary["access_token"];
                user.RefreshToken = tokenDictionary["refresh_token"];
                user.Id = tokenDictionary["userid"];
                user.ConnectId = tokenDictionary["connectid"];
                RefreshClient(false);
                InitHttpClient();
                InitChatConnect();
                refreshTimer.Change(RefreshTimeOutMsec - 5000, RefreshTimeOutMsec);
                datesynctimer.Change(100, 3600000);
            }
            return new SsfActionResult() { Error = err, Message = ErrorMess.Messages[err] };
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
        public SsfActionResult Login(UserLogin userLogin)
        {
            user = new UserLogin() { Name = userLogin.Name, Password = userLogin.Password, Gameid = _gameID, Grant_type = "password", TimeOffset = DateTimeOffset.Now.Offset.Hours, DeviceId = userLogin.DeviceId };
            if (string.IsNullOrWhiteSpace(userLogin.Name) || string.IsNullOrWhiteSpace(userLogin.Password))
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
                if (!tokenDictionary.ContainsKey("connectid") || string.IsNullOrEmpty(tokenDictionary["connectid"]))
                    throw new Exception("Error connectid");

                int RefreshTimeOutMsec = 86400000;
                if (err == ERROR.NOERROR)
                {
                    RefreshTimeOutMsec = int.Parse(tokenDictionary["refreshtimeout"]);
                    user.RefreshToken = tokenDictionary["refresh_token"];
                    user.AccessToken = tokenDictionary["access_token"];
                    user.WsAccessToken = tokenDictionary["wsacess_token"];
                    user.ConnectId = tokenDictionary["connectid"];
                    user.Id = tokenDictionary["userid"];
                    refreshTimer.Change(RefreshTimeOutMsec - 5000, RefreshTimeOutMsec);
                    RefreshClient(false);
                    InitHttpClient();
                    InitChatConnect();
                }
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
        private SsfActionResult RefreshClient(bool needReconnect = true)
        {
            string result;
            ERROR err = ERROR.NOERROR;
            int RefreshTimeOutMsec = 86400000;
            HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.RefreshToken);
            using (var response = PostAsJson(client, $"{OfficeUrl}/RefreshToken", null))
            {
                if (response.StatusCode != HttpStatusCode.OK) return new SsfActionResult() { Error = ERROR.NETERROR, Message = $"{ErrorMess.Messages[ERROR.NETERROR]} {response.StatusCode.ToString()}" };
                result = response.Content.ReadAsStringAsync().Result;
            }
            Dictionary<string, string> tokenDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(result);
            err = (ERROR)int.Parse(tokenDictionary["Error"]);
            if (err != ERROR.NOERROR)
                return new SsfActionResult() { Error = err, Message = ErrorMess.Messages[err] };

            RefreshTimeOutMsec = int.Parse(tokenDictionary["refreshtimeout"]);
            user.AccessToken = tokenDictionary["access_token"];
            user.RefreshToken = tokenDictionary["refresh_token"];
            user.WsAccessToken = tokenDictionary["wsacess_token"];

            refreshTimer.Change(RefreshTimeOutMsec - 5000, RefreshTimeOutMsec);
            datesynctimer.Change(100, 3600000);
            UpdateAccessToken?.Invoke(user.AccessToken);
            if (needReconnect)
            {
                InitHttpClient();
                InitChatConnect();
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
        #endregion
        #region ServiceMethods
        private void CommonCallBack(string method, object arg1, Action callback)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        NestedActions.TryRemove((int)index, out _);
                        callback?.Invoke();
                    }
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
        private void CommonCallBack(string method, object arg1, object arg2, Action callback)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1, arg2).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        NestedActions.TryRemove((int)index, out _);
                        callback?.Invoke();
                    }
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }

        private void CommonCallBackWithResult<T>(string method, Action<T> callback)
        {
            var actionIndex =  NestedActions.Count;
            var requestid = Guid.NewGuid().ToString();
            var responceMethodName = $"{method}{requestid}";
            Action action = new Action(() =>
            {
                chatconnect.On<T>(responceMethodName, o =>
                {
                    chatconnect.Remove(responceMethodName);
                    callback?.Invoke(o);
                });
                chatconnect.InvokeAsync(method, requestid).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }    
        private void CommonCallBackWithResult<T>(string method, object arg1, Action<T> callback)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(async () =>
            {
                var requestid = Guid.NewGuid().ToString();
                var responceMethodName = $"{method}{requestid}" ;
                chatconnect.On<T>(responceMethodName, o =>
                {
                    chatconnect.Remove(responceMethodName);
                    callback?.Invoke(o);
                });
                await chatconnect.InvokeAsync(method, arg1, requestid).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
        private void CommonCallBackWithResult<T>(string method, object arg1, object arg2, Action<T> callback)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                var requestid = Guid.NewGuid().ToString();
                var responceMethodName = $"{method}{requestid}";
                chatconnect.On<T>(responceMethodName, o =>
                {
                    chatconnect.Remove(responceMethodName);
                    callback?.Invoke(o);
                });
                chatconnect.InvokeAsync(method, arg1, arg2, requestid).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            var actionToExec = NestedActions.Values.Last();
            actionToExec?.Invoke();
            //action?.Invoke();
        }
        private void CommonCallBackWithResult<T>(string method, object arg1, object arg2, object arg3, Action<T> callback)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                var requestid = Guid.NewGuid().ToString();
                var responceMethodName = $"{method}{requestid}";
                chatconnect.On<T>(responceMethodName, o =>
                {
                    chatconnect.Remove(responceMethodName);
                    callback?.Invoke(o);
                });
                chatconnect.InvokeAsync(method, arg1, arg2, arg3, requestid).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }

        private void CommonWebSockAction(string method, object arg1)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
        private void CommonWebSockAction(string method, object arg1, object arg2)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1, arg2).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
        private void CommonWebSockAction(string method, object arg1, object arg2, object arg3)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1, arg2, arg3).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
        private void CommonWebSockAction(string method, object arg1, object arg2, object arg3, object arg4)
        {
            var actionIndex = NestedActions.Count;
            Action action = new Action(() =>
            {
                chatconnect.InvokeAsync(method, arg1, arg2, arg3, arg4).ContinueWith((t, index) =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        NestedActions.TryRemove((int)index, out _);
                }, actionIndex);
            });
            NestedActions.TryAdd(actionIndex, action);
            action?.Invoke();
        }
          
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

        #region CommonOperation
        private string GetData(string method, bool needAuth = true)
        {
            string getrequest = $"{DataUrl}{method}";
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
        public static Uri UriToBuyCurrency(int volume) => new Uri($"{OfficeUrl}/{LinkToPay}&val={volume}");
        private async void SyncDate(object obj)
        {
            var sd = await GetServerDateAsync();
            var ld = DateTime.Now;
            DateOffset = ld.Subtract(sd);
        }

        private void GetServerDateCallBack(Action<DateTime> action) => CommonCallBackWithResult<DateTime>("Basedate", o => action?.Invoke(o));
        private DateTime GetServerDate()
        {
            var rs = GetData($"basedate", false);
            DateTime res = JsonConvert.DeserializeObject<DateTime>(rs);
            return res;
        }
        private async Task<DateTime> GetServerDateAsync()
        {
            var rs = await GetDataAsync($"basedate", false);
            DateTime res = JsonConvert.DeserializeObject<DateTime>(rs);
            return res;
        }
        public DateTime SyncServerDate => DateTime.Now.Subtract(DateOffset);

        public void GetCurrencyNameCallBack(Action<string> action) => CommonCallBackWithResult<string>("GetCurrencyName", o => action?.Invoke(o));
        public string GetCurrencyName() => GetData("getcname");
        public async Task<string> GetCurrencyNameAsync() => await GetDataAsync("getcname");

        public void GetGameParamsCallBack(Action<IEnumerable<CompParameter>> action) => CommonCallBackWithResult<IEnumerable<CompParameter>>("GetGameParams", o => action?.Invoke(o));
        public IEnumerable<CompParameter> GetGameParams() => JsonConvert.DeserializeObject<IEnumerable<CompParameter>>(GetData($"GetGameParams"));
        public async Task<List<CompParameter>> GetGameParamsAsync() => JsonConvert.DeserializeObject<List<CompParameter>>(await GetDataAsync($"GetGameParams"));

        public void SendGamerEndOfFrameWebSock(UserInCompState nextState) => CommonWebSockAction("GamerEndsFrame", nextState);

        public void GetActiveDetailsCallBack(string activeid, DateTime start, DateTime end, Action<IEnumerable<UserInGameActiveMoving>> action) =>
            CommonCallBackWithResult<IEnumerable<UserInGameActiveMoving>>("GetActiveDetails", activeid, start.ToString("yyyyMMdd"), end.ToString("yyyyMMdd"), o => action?.Invoke(o));
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

        public void GetActivesCallBack(DateTime date, Action<List<GamerActive>> action) => CommonCallBackWithResult<List<GamerActive>>("GetActives", date, o => action?.Invoke(o));
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

        public void GetParamValueCallBack(string id, Action<int> action) => CommonCallBackWithResult<int>("GetParamValue", id, o => action?.Invoke(o));
        public int GetParamValue(string id) => JsonConvert.DeserializeObject<int>(GetData($"GetParamValue/{id}"));
        public async Task<int> GetParamValueAsync(string id) => JsonConvert.DeserializeObject<int>(await GetDataAsync($"GetParamValue/{id}"));
        #endregion

        #region CommonCompetition
        public void SubscribeToTournirWebSock(string id) => CommonWebSockAction("SubscribeToTournir", id);
        public void UnSubscribeFromCompetitionWebSock(string id) => CommonWebSockAction("UnSubscribeFromCompetition", id);

        public void WriteFrameResultWebSock(FrameResult frameresults) => CommonWebSockAction("WriteFrameResult", frameresults);
        public SsfActionResult WriteFrameResult(FrameResult frameresults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteFrameResult", frameresults))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void WriteFrameResultsWebSock(List<FrameResult> frameresults) => CommonWebSockAction("WriteFrameResults", frameresults);
        public SsfActionResult WriteFrameResults(List<FrameResult> frameresults)
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

        public void WriteFrameReadMatchResultsCallBack(List<FrameResult> frameresults, Action<List<FrameResult>> action) =>
            CommonCallBackWithResult<List<FrameResult>>("WriteFrameReadMatchResults", frameresults, o => action?.Invoke(o));
        public SsfActionResult WriteFrameReadMatchResults(List<FrameResult> frameresults, out List<FrameResult> matchresults)
        {
            matchresults = new List<FrameResult>();
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteFrameReadMatchResults", frameresults))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var res = responce.Content.ReadAsStringAsync().Result;
                matchresults = JsonConvert.DeserializeObject<List<FrameResult>>(res);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<List<FrameResult>> WriteFrameReadMatchResultsAsync(List<FrameResult> frameresults)
        {
            List<FrameResult> matchresults = null;
            using (var responce = await PostAsJsonAsync(httpClient, $"{DataUrl}WriteFrameReadMatchResults", frameresults))
            {
                if (responce.IsSuccessStatusCode)
                {
                    var res = await responce.Content.ReadAsStringAsync();
                    matchresults = JsonConvert.DeserializeObject<List<FrameResult>>(res);
                }
            }
            return matchresults;
        }

        public void WriteFreeMatchWinnerWebSock(FreeMatchResult matchResults) => CommonWebSockAction("WriteFreeMatchWinner", matchResults);
        public SsfActionResult WriteFreeMatchWinner(string uid, FreeMatchResult matchResults)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteFreeMatchWinner", new { uid, matchResults }))
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
        public void GamerStartsFrameWebSock(string placeid, string customparams) => CommonWebSockAction("GamerStartsFrame", placeid, customparams);
        public void StartMatchFrameWebSock(string placeid, UserInCompState state) => CommonWebSockAction("StartMatchFrame", placeid, state);
        public void SetTournRoundStartWebSock(RoundStart start) => CommonWebSockAction("SetTournRoundStart", start);
        public void EndOfRoundWebSock(string placeid, string winnerid) => CommonWebSockAction("EndOfRound", placeid, winnerid);

        public void GetTournamentsCallBack(Action<List<Tournament>> action) => CommonCallBackWithResult<List<Tournament>>("Tournaments", o => action?.Invoke(o));
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
        public List<GamerFrameResult> GetTournirPlaceResults(string placeid)
        {
            var p = GetData($"GetTournirPlaceResults/{placeid}");
            var res = JsonConvert.DeserializeObject<List<GamerFrameResult>>(p);
            return res;
        }
        public async Task<List<GamerFrameResult>> GetTournirPlaceResultsAsync(string placeid)
        {
            var p = await GetDataAsync($"GetTournirPlaceResults/{placeid}");
            var res = JsonConvert.DeserializeObject<List<GamerFrameResult>>(p);
            return res;
        }

        #endregion
        #region FreeMatches
        public void UnSubscribeFromFreeMatchWebSock(string cid) => CommonWebSockAction("UnSubscribeFromFreeMatch", cid);
        public void SubscribeToFreeMatchWebSock(string cid) => CommonWebSockAction("SubscribeToFreeMatch", cid);
        public void StartFreeMatchWebSock(string compid) => CommonWebSockAction("StartFreeMatch", compid);
        public void DelFreeMatchWebSock(string compid) => CommonWebSockAction("DelFreeMatch", compid);

        public void GetMatchResultsDetailCallBack(string matchid, Action<IEnumerable<FrameResult>> action) =>
            CommonCallBackWithResult<IEnumerable<FrameResult>>("GetMatchResultsDetail", matchid, o => action?.Invoke(o));
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

        public void GetFreeMatchesCallBack(Action<List<FreeMatch>> action) => CommonCallBackWithResult<List<FreeMatch>>("FreeMatches", o => action?.Invoke(o));
        public List<FreeMatch> GetFreeMatches()
        {
            var p = GetData($"FreeMatches");
            var res = JsonConvert.DeserializeObject<List<FreeMatch>>(p);
            return res;
        }

        public void GetFreeMatchCallBack(string compid, Action<FreeMatch> action) => CommonCallBackWithResult<FreeMatch>("GetFreeMatch", compid, o => action?.Invoke(o));
        public async Task<FreeMatch> GetFreeMatchAsync(string compid)
        {
            var p = await GetDataAsync($"GetFreeMatch/{compid}");
            var res = JsonConvert.DeserializeObject<FreeMatch>(p);
            return res;
        }
        public FreeMatch GetFreeMatch(string compid)
        {
            var p = GetData($"GetFreeMatch/{compid}");
            var res = JsonConvert.DeserializeObject<FreeMatch>(p);
            return res;
        }

        public void GetFreeMatchStateCallBack(string compid, Action<FreeMatchState> action) => CommonCallBackWithResult<FreeMatchState>("GetFreeMatchState", compid, s => action?.Invoke(s));

        public async Task<FreeMatchState> GetFreeMatchStateAsync(string compid)
        {
            var p = await GetDataAsync($"GetFreeMatchState/{compid}");
            var res = JsonConvert.DeserializeObject<FreeMatchState>(p);
            return res;
        }
        public FreeMatchState GetFreeMatchState(string compid)
        {
            var p = GetData($"GetFreeMatchState/{compid}");
            var res = JsonConvert.DeserializeObject<FreeMatchState>(p);
            return res;
        }


        public async Task<List<FreeMatch>> GetFreeMatchesAsync()
        {
            var p = await GetDataAsync($"FreeMatches");
            var res = JsonConvert.DeserializeObject<List<FreeMatch>>(p);
            return res;
        }
        public List<CompApplicant> GetFreeMatchParticipants()
        {
            var p = GetData($"GetFreeMatchParticipants");
            var res = JsonConvert.DeserializeObject<List<CompApplicant>>(p);
            return res;
        }
        public async Task<List<CompApplicant>> GetFreeMatchParticipantssAsync()
        {
            var p = await GetDataAsync($"GetFreeMatchParticipants");
            var res = JsonConvert.DeserializeObject<List<CompApplicant>>(p);
            return res;
        }

        public List<StringAndInt> GetMatchScore(string matchid)
        {
            var p = GetData($"GetMatchScore/{matchid}");
            return JsonConvert.DeserializeObject<List<StringAndInt>>(p);
        }
        public async Task<List<StringAndInt>> GetMatchScoreAsync(string matchid)
        {
            var p = await GetDataAsync($"GetMatchScore/{matchid}");
            return JsonConvert.DeserializeObject<List<StringAndInt>>(p);
        }
        public void GetMatchScoreCallBack(string matchid, Action<List<StringAndInt>> action) => CommonCallBackWithResult<List<StringAndInt>>("GetMatchScore", matchid, r => action.Invoke(r));


        public List<StringAndInt> GetChampMatchScore(string matchid)
        {
            var p = GetData($"GetChampMatchScore/{matchid}");
            return JsonConvert.DeserializeObject<List<StringAndInt>>(p);
        }
        public async Task<List<StringAndInt>> GetChampMatchScoreAsync(string matchid)
        {
            var p = await GetDataAsync($"GetChampMatchScore/{matchid}");
            return JsonConvert.DeserializeObject<List<StringAndInt>>(p);
        }
        public void GetChampMatchScoreCallBack(string matchid, Action<List<StringAndInt>> action) => CommonCallBackWithResult<List<StringAndInt>>("GetChampMatchScore", matchid, r => action.Invoke(r));

        #endregion
        #region Champs
        public void SubscribeToChampWebSock(string id) => CommonWebSockAction("SubscribeToChamp", id);

        public void GetChampsCallBack(Action<List<Champ>> action) => CommonCallBackWithResult<List<Champ>>("Champs", o => action?.Invoke(o));
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

        public void GetChampPlacesCallBack(string champid, Action<List<CompPlace>> action) => CommonCallBackWithResult<List<CompPlace>>("GetChampPlaces", champid, o => action?.Invoke(o));
        public async Task<List<CompPlace>> GetChampPlacesAsync(string champid)
        {
            var p = await GetDataAsync($"GetChampPlaces/{champid}");
            var res = JsonConvert.DeserializeObject<List<CompPlace>>(p);
            return res;
        }
        public List<CompPlace> GetChampPlaces(string champid)
        {
            var p = GetData($"GetChampPlaces/{champid}");
            var res = JsonConvert.DeserializeObject<List<CompPlace>>(p);
            return res;
        }

        public void StartChampPlaceWebSock(string placeid) => CommonWebSockAction("StartChampPlace", placeid);

        public void WriteChampPlaceScoreWebSock(ChampPlaceScore placescore) => CommonWebSockAction("WriteChampPlaceScore", placescore);
        public SsfActionResult WriteChampPlaceScore(ChampPlaceScore placescore)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteChampPlaceScore", placescore))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void WriteFreeMatchScoreWebSock(FreeMatchScore freeMatchScore) => CommonWebSockAction("WriteFreeMatchScore", freeMatchScore);
        public void WriteFreeMatchScoreCallBack(FreeMatchScore freeMatchScore, Action action) => CommonCallBack("WriteFreeMatchScore", freeMatchScore, action);

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


        public void WriteChampFrameScoreWebSock(ChampFrameResult champFrameResult) => CommonWebSockAction("WriteChampFrameScore", champFrameResult);
        public SsfActionResult WriteChampFrameScore(ChampFrameResult champFrameResult)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}WriteChampFrameScore", champFrameResult))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }

        public void GetChampResultsDetailCallBack(string compid, Action<IEnumerable<FrameResult>> action) =>
            CommonCallBackWithResult<IEnumerable<FrameResult>>("GetChampResultsDetail", compid, o => action?.Invoke(o));
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

        public void GetAnterResultsDetailCallBack(string compid, Action<IEnumerable<FrameResult>> callback) =>
            CommonCallBackWithResult<IEnumerable<FrameResult>>("GetAnterResultsDetail", compid, o => callback?.Invoke(o));
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

        public void StartAnterFrameCallback(string compid, Action<AnterApplicant> callback) =>
            CommonCallBackWithResult<AnterApplicant>("StartAnterFrame", compid, o => callback?.Invoke(o));
        public async Task<AnterApplicant> StartAnterFrameAsync(string compid)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}StartAnterFrame", compid))
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
        public AnterApplicant StartAnterFrame(string compid)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}StartAnterFrame", compid))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var respdata = responce.Content.ReadAsStringAsync().Result;
                var anterApplicant = JsonConvert.DeserializeObject<AnterApplicant>(respdata);
                return anterApplicant;
            }
        }

        public void WriteAnterPrefsWebSock(List<AnterResult> champresults) => CommonWebSockAction("WriteAnterPrefs", champresults);
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

        public void GetChatCallBack(string neghbid, string lastmessageid, Action<List<Chat>> action) =>
            CommonCallBackWithResult<List<Chat>>("GetChatHist", neghbid, lastmessageid, o =>  action?.Invoke(o.ToList()));
        public async Task<List<Chat>> GetChatAsync(string neghbid, string lastmessageid)
        {
            var cnt = await GetDataAsync($"GetChatHist/{neghbid}/{lastmessageid}");
            var rss = JsonConvert.DeserializeObject<List<Chat>>(cnt);
            return rss;
        }
        public List<Chat> GetChat(string neghbid, string lastmessageid)
        {
            string res = GetData($"GetChatHist/{neghbid}/{lastmessageid}");
            var rss = JsonConvert.DeserializeObject<List<Chat>>(res);
            return rss;
        }

        public void GetInterlocutorsCallBack(Action<List<Interlocutor>> action) =>
            CommonCallBackWithResult<List<Interlocutor>>("GetInterlocutors", o => action?.Invoke(o)); 
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
        public void GetAllChatCountCallBack(Action<int> action) =>  CommonCallBackWithResult<int>("GetAllChatCount", o => action?.Invoke(o));
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

        public void GetExchangeEntitiesCallBack(Action<List<ExchangeEntity>> action) =>
            CommonCallBackWithResult<List<ExchangeEntity>>("GetExchangeEntities", o => action?.Invoke(o));
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

        public void GetOffersCallBack(Action<List<ExchangeOffer>> action) => CommonCallBackWithResult<List<ExchangeOffer>>("GetOffers", o => action?.Invoke(o));
        public async Task<List<ExchangeOffer>> GetOffersAsync()
        {
            var p = await GetDataAsync($"GetOffers");
            return JsonConvert.DeserializeObject<List<ExchangeOffer>>(p);
        }
        public List<ExchangeOffer> GetOffers()
        {
            var p = GetData($"GetOffers");
            return JsonConvert.DeserializeObject<List<ExchangeOffer>>(p);
        }

        public void ChangeOfferCallBack(List<ExchangeOfferGood> ChangeOffer, Action<ExchangeUpdateResult> action) => CommonCallBackWithResult<ExchangeUpdateResult>("ChangeOffer", ChangeOffer, o => action?.Invoke(o));
        public SsfActionResult ChangeOffer(List<ExchangeOfferGood> ChangeOffer, out ExchangeUpdateResult result)
        {
            result = new ExchangeUpdateResult() { ChangeState = -4 };
            using (var responce = PostAsJson(httpClient, $"{DataUrl}ChangeOffer", ChangeOffer))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.WRONGARGUMENTS, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
                var res = responce.Content.ReadAsStringAsync().Result;
                result = JsonConvert.DeserializeObject<ExchangeUpdateResult>(res);
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task<ExchangeUpdateResult> ChangeOfferAsync(List<ExchangeOfferGood> ChangeOffer)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}ChangeOffer", ChangeOffer))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new ExchangeUpdateResult() {ChangeState = -4 };
                }
                var res = await responce.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<ExchangeUpdateResult>(res);
                return result;
            }
        }

        public void FindOffersCallBack(List<string> goodids, Action<List<ExchangeOffer>> action) => CommonCallBackWithResult<List<ExchangeOffer>>("FindOffers", goodids, o => action?.Invoke(o));
        public async Task<List<ExchangeOffer>> FindOffersAsync(List<string> goodids)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}FindOffers", goodids))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var res = await responce.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<List<ExchangeOffer>>(res);
                return result;
            }
        }
        public List<ExchangeOffer> FindOffers(List<string> goodids)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}FindOffers", goodids))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return null;
                }
                var res = (responce.Content.ReadAsStringAsync()).Result;
                var result = JsonConvert.DeserializeObject<List<ExchangeOffer>>(res);
                return result;
            }
        }

        public void BuyOfferCallBack(OfferState offerState, Action<OfferState> action) => CommonCallBackWithResult<OfferState>("BuyOffer", offerState, o => action?.Invoke(o));
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

        public void GetNewOfferActivesCallBack(string oid, Action<List<ActiveType>, string> action) => CommonCallBackWithResult<List<ActiveType>>("GetNewOfferActives", oid, o => action?.Invoke(o, oid));
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

        public void GetOfferContentCallBack(ExchangeOffer offer, Action<List<ExchangeOfferGood>, ExchangeOffer> action) => 
            CommonCallBackWithResult<List<ExchangeOfferGood>>("OfferContent", offer.OfferId, offer.SellerId == Id, o => action?.Invoke(o, offer));
        public async Task<List<ExchangeOfferGood>> GetOfferContentAsync(string oid)
        {
            var p = await GetDataAsync($"OfferContent/{oid}/{true}");
            var res = JsonConvert.DeserializeObject<List<ExchangeOfferGood>>(p);
            return res;
        }
        public List<ExchangeOfferGood> GetOfferContent(string oid)
        {
            var p = GetData($"OfferContent/{oid}/{true}");
            var res = JsonConvert.DeserializeObject<List<ExchangeOfferGood>>(p);
            return res;
        }

        public void GetAnteriorsCallBack(Action<List<Anterior>> action) => CommonCallBackWithResult<List<Anterior>>("GetAnteriors", o => action?.Invoke(o));
        public async Task<List<Anterior>> GetAnteriorsAsync()
        {
            var p = await GetDataAsync($"GetAnteriors");
            var res = JsonConvert.DeserializeObject<List<Anterior>>(p);
            return res;
        }
        public List<Anterior> GetAnteriors()
        {
            var p = GetData($"GetAnteriors");
            var res = JsonConvert.DeserializeObject<List<Anterior>>(p);
            return res;
        }

        public void GetGamerAchivmentsCallBack(Action<List<GamerAchivment>> action) => CommonCallBackWithResult<List<GamerAchivment>>("GetGamerAchivments", o => action?.Invoke(o));
        public async Task<List<GamerAchivment>> GetGamerAchivmentsAsync()
        {
            var p = await GetDataAsync($"GetGamerAchivments");
            var res = JsonConvert.DeserializeObject<List<GamerAchivment>>(p);
            return res;
        }
        public List<GamerAchivment> GetGamerAchivments()
        {
            var p = GetData($"GetGamerAchivments");
            var res = JsonConvert.DeserializeObject<List<GamerAchivment>>(p);
            return res;
        }


        public void GetAnteriorApplicantsCallBack(string compid, Action<List<AnterApplicant>> action) => 
            CommonCallBackWithResult<List<AnterApplicant>>("GetAnteriorApplicants", compid, o => action?.Invoke(o));
        public List<AnterApplicant> GetAnteriorApplicants(string compid)
        {
            var p = GetData($"GetAnteriorApplicants/{compid}");
            var res = JsonConvert.DeserializeObject<List<AnterApplicant>>(p);
            return res;
        }
        public async Task<List<AnterApplicant>> GetAnteriorApplicantsAsync(string compid)
        {
            var p = await GetDataAsync($"GetAnteriorApplicants/{compid}");
            var res = JsonConvert.DeserializeObject<List<AnterApplicant>>(p);
            return res;
        }

        public void GetTournirPlaceNeigborsCountCallBack(string placeid, Action<int> action) => 
            CommonCallBackWithResult<int>("GetTournirPlaceNeigborsCount", placeid, o => action?.Invoke(o));
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


        public void SaveFrameFreeParamWebSock(FrameParam inframeParam) => CommonWebSockAction("SaveFrameFreeParam", inframeParam);
        public SsfActionResult SaveFrameFreeParam(FrameParam inframeParam)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}SaveFrameFreeParam", inframeParam))
            {
                if (!responce.IsSuccessStatusCode)
                {
                    return new SsfActionResult() { Error = ERROR.NETERROR, Message = ErrorMess.Messages[ERROR.NETERROR] };
                }
            }
            return new SsfActionResult() { Error = ERROR.NOERROR, Message = ErrorMess.Messages[ERROR.NOERROR] };
        }
        public async Task SaveFrameFreeParamAsync(FrameParam inframeParam)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}SaveFrameFreeParam", inframeParam)) { }
        }

        public async Task<List<FrameParam>> GetFrameFreeParamAsync(string placeid, int framenum)
        {
            var p = await GetDataAsync($"GetFrameFreeParam/{placeid}/{framenum}");
            var res = JsonConvert.DeserializeObject<List<FrameParam>>(p);
            return res;
        }
        public List<FrameParam> GetFrameFreeParam(string placeid, int framenum)
        {
            var p = GetData($"GetFrameFreeParam/{placeid}/{framenum}");
            var res = JsonConvert.DeserializeObject<List<FrameParam>>(p);
            return res;
        }
        public void GetFrameFreeParamCallBack(string placeid, int framenum, Action<List<FrameParam>> action) =>
            CommonCallBackWithResult<List<FrameParam>>("GetFrameFreeParam", placeid, framenum, o => action?.Invoke(o));

        public void AddFreeMatchWebSock(FreeMatch freematch) => CommonWebSockAction("NewFreeMatch", freematch);
        public async Task AddFreeMatchAsync(FreeMatch freematch) =>  await PostAsJsonAsync($"{DataUrl}NewFreeMatch", freematch);
        public void AddFreeMatch(FreeMatch freematch) => PostAsJson($"{DataUrl}NewFreeMatch", freematch);

        public async Task AddOrderToActivityAsync(string Id, int volume, string comment, int iscurrency)
        {
           await PostAsJsonAsync($"{DataUrl}AddOrder", new Order() { Id = Id, Volume = volume, Comment = comment, IsCurrency = iscurrency });
        }
        public void AddOrderToActivity(string Id, int volume, string comment, int iscurrency)
        {
            PostAsJson($"{DataUrl}AddOrder", new Order() { Id = Id, Volume = volume, Comment = comment, IsCurrency = iscurrency });
        }
        public void AddOrderToActivityWebSock(string Id, int volume, string comment, int iscurrency) => CommonWebSockAction("AddOrder", new Order() { Id = Id, Volume = volume, Comment = comment, IsCurrency = iscurrency });
        public async Task ChangeRatingAsync(ChangeRatingRequest ratingRequest)
        {
            await PostAsJsonAsync($"{DataUrl}ChangeRating", ratingRequest);
        }
        public void ChangeRating(ChangeRatingRequest ratingRequest)
        {
            PostAsJson($"{DataUrl}ChangeRating", ratingRequest);
        }

        public void ChangeRatingWebSock(ChangeRatingRequest ratingRequest) =>  CommonWebSockAction("ChangeRating", ratingRequest);
        public void BuyActiveCallBack(string Id, int volume, Action callback) =>   CommonCallBack("BuyActive", new Order() { Id = Id, Volume = volume }, callback);        
        public SsfActionResult BuyActive(string Id, int volume)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}BuyActive", new Order() { Id = Id, Volume = volume }))
            {
                if (responce.IsSuccessStatusCode)
                {
                    if (int.TryParse(responce.Content.ReadAsStringAsync().Result, out var error))
                        return new SsfActionResult() { Error = (ERROR)error, Message = ErrorMess.Messages[(ERROR)error] };
                    else
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
                else
                {
                    return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
            }
        }
        public async Task<SsfActionResult> BuyActiveAsync(string Id, int volume)
        {
            using (var responce = await PostAsJsonAsync($"{DataUrl}BuyActive", new Order() { Id = Id, Volume = volume }))
            {
                if (responce.IsSuccessStatusCode)
                {
                    if (int.TryParse(await responce.Content.ReadAsStringAsync(), out var error))
                        return new SsfActionResult() { Error = (ERROR)error, Message = ErrorMess.Messages[(ERROR)error] };
                    else
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
                else
                {
                    return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
            }
        }
        public void GetUserGamesNumberCallBack(Action<int> action) =>
            CommonCallBackWithResult<int>("GetUserGamesNumber", result => action?.Invoke(result));
        public async Task<int> GetUserGamesNumberAsync()
        {
            var p = await GetDataAsync($"GetUserGamesNumber");
            var res = JsonConvert.DeserializeObject<int>(p);
            return res;
        }
        public int GetUserGamesNumber()
        {
            var p = GetData($"GetUserGamesNumber");
            var res = JsonConvert.DeserializeObject<int>(p);
            return res;
        }

        public void DoExchangeCallBack(ExchangeOrder exchangeOrder, Action<ExchangeResult> action) =>
            CommonCallBackWithResult<ExchangeResult>("DoExchange", exchangeOrder, o => action?.Invoke(o));
        public async Task<SsfActionResult> DoExchangeAsync(ExchangeOrder exchangeOrder)
        {
            using (var responce = await PostAsJsonAsync(httpClient, $"{DataUrl}DoExchange", exchangeOrder))
            {
                if (responce.IsSuccessStatusCode)
                {
                    if (int.TryParse(await responce.Content.ReadAsStringAsync(), out var error))
                        return new SsfActionResult() { Error = (ERROR)error, Message = ErrorMess.Messages[(ERROR)error] };
                    else
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
                else
                {
                    return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }
            }
        }
        public SsfActionResult DoExchange(ExchangeOrder exchangeOrder)
        {
            using (var responce = PostAsJson(httpClient, $"{DataUrl}DoExchange", exchangeOrder))
            {
                if (responce.IsSuccessStatusCode)
                {
                    if (int.TryParse(responce.Content.ReadAsStringAsync().Result, out var error ) )
                        return new SsfActionResult() { Error = (ERROR)error, Message = ErrorMess.Messages[(ERROR)error] };
                    else
                        return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };                    
                }
                else
                {
                    return new SsfActionResult() { Error = ERROR.COMMONERROR, Message = ErrorMess.Messages[ERROR.COMMONERROR] };
                }               
            }
        }
        #endregion

        #region Chat

        public void SetMessReadedWebSock(string messid) => CommonWebSockAction("SetMessReaded", messid);
        public void SetMessReaded(string messid)
        {
            var responce = PostAsJson(httpClient, $"{DataUrl}SetMessReaded", messid);
        }
        public async Task SetMessReadedAsync(string messid)
        {
            var responce = await PostAsJsonAsync($"{DataUrl}SetMessReaded", messid);
        }
        public void ChooseCompetitionTypeWebSock(COMPTYPE comptype) => CommonWebSockAction("EntersToCompType", comptype);
        public void LeaveCompTypeWebSock(COMPTYPE comptype) => CommonWebSockAction("LeaveCompType", comptype);
        public void ChangeChampPlaceWebSock(string placeid) => CommonWebSockAction("ChangeChampPlace", placeid);
        public void EnterToCompetitionWebSock(string cid) => CommonWebSockAction("EnterToCompetition", cid);
        public void LeaveCompetitionWebSock(string cid) => CommonWebSockAction("LeaveCompetition", cid);
        public void SendMessageWebSock(string recipientId, string text) => CommonWebSockAction("TransMessageFC", recipientId, text);
        public void SendMessageCallBack(string recipientId, string text, Action callback) => CommonCallBack("TransMessageFC", recipientId, text, callback);
        public void SendMessageToManyWebSock(List<string> recipientIds, string text) => CommonWebSockAction("TransMessageFCToMany", recipientIds, text);
        public void SendMessageToManyCallBack(List<string> recipientIds, string text, Action callback) => 
            CommonCallBack("TransMessageFCToMany", recipientIds, text, callback);


        /// <summary>
        /// Произвольное сообщение другим клиентам из списка SsfConnectIds и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborMethodWebSock(string[] SsfConnectIds, bool needBack, string methodname, FreeMesssage methodparams) =>  CommonWebSockAction("InvokeNeigborMethod", SsfConnectIds, needBack, methodname, methodparams);
        /// <summary>
        /// Произвольное сообщение другим клиентам из списка SsfConnectIds и себе (если needBack = true). Выполняется немедленно. Без постановки в очередь.
        /// 
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborMethodWebSockDirect(string[] SsfConnectIds, bool needBack, string methodname, FreeMesssage methodparams) => chatconnect.InvokeAsync("InvokeNeigborMethod", SsfConnectIds, needBack, methodname, methodparams);
        
        /// <summary>
        /// Произвольное сообщение партнерам по типу соревнования и состояния в нем и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborsTheSameCompTypeAndStateWebSock(bool needBack, string methodname, FreeMesssage methodparams) =>  CommonWebSockAction("InvokeNeigborsSCaS", needBack, methodname, methodparams);

        /// <summary>
        /// Произвольное сообщение партнерам по типу соревнования и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborsTheSameCompTypeWebSock(bool needBack, string methodname, FreeMesssage methodparams) => CommonWebSockAction("InvokeNeigborsSCaS", needBack, methodname, methodparams);
        /// <summary>
        /// <summary>
        /// Произвольное сообщение партнерам по соревнованию и себе (если needBack = true)
        /// </summary>
        /// <param name="SsfConnectIds"></param>
        /// <param name="methodname"></param>
        /// <param name="methodparams"></param>
        /// <returns></returns>
        public void InvokeNeigborsTheSameCompWebSock(bool needBack, string methodname, FreeMesssage methodparams) => CommonWebSockAction("InvokeNeigborsSTCaS", needBack, methodname, methodparams);
        public void InvokeNeigborsTheSameCompCustomStateWebSock(bool needBack, string methodname, UserInCompState state, FreeMesssage methodparams) => CommonWebSockAction("InvokeNeigborsSTCaCS", needBack, methodname, state, methodparams);
        #endregion
        private void InitHttpClient()
        {           
             httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        }
    }

    public class NamedAction<T>
    {
        public string Name { get; set; }
        public Action<T> Action { get; set; }
    }

}
