using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace GBOClientStd
{
    public class UserLogin
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public string Grant_type { get; set; }
        public string Gameid { get; set; }
        public string Id { get; set; }
        public string PhoneNumber { get; set; }
        public string ConnectId { get; set; }
        public string AccessToken { get; set; }
        public string WsAccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int TimeOffset { get; set; }

    }
    public class Interlocutor
    {
        public string Name { get; set; }
        public string Id { get; set; }
        //public bool Online { get; set; }
        public string SrConnectID { get; set; }
        public int MessCount { get; set; }
    }
    public class UserRegister : UserLogin
    {
        public string Email { get; set; }
    }
    public interface IUserGameResource
    {
        string Name { get; set; }
        string Id { get; set; }
        int? Value { get; set; }
        IUserGameResource Childrens { get; set; }
    }
    

    public interface ISsfActionResult
    {
        ERROR Error { get; set; }
        string Message { get; set; }
    }
    public class SsfActionResult : ISsfActionResult
    {
        public ERROR Error { get; set; }
        public string Message { get; set; }
    }
    public class UserInGameActiveMoving
    {
        public DateTime OrderDate { set; get; }
        public int Volume { get; set; }
        public string Comment { get; set; }
    }
    public class Order
    {
        public string Id { get; set; }
        public int Volume { get; set; }
        public string Comment { get; set; }
    }
    public class Offer
    {
        public string OfferId { get; set; }
        public string OfferNum { get; set; }
        public string SellerId { get; set; }
        public string SellerName { get; set; }
        public decimal FullCost { get; set; }
        public DateTime? CreateDate { get; set; }
        public bool IsNew { get; set; } = false;
        public int Editable { get; set; }
    }

    public class StringAndDecimal
    {
        public string S { get; set; }
        public decimal D { get; set; }
    }
    public class Good
    {
        public string GoodId { get; set; }
        public string OfferId { get; set; }
        public string ActiveId { get; set; }
        public string ActiveName { get; set; }
        public int Volume { get; set; }
        public int MaxVol { get; set; }
        public decimal Cost { get; set; }
        public decimal FullCost => Volume * Cost;
        public ActiveType ActiveType { get; set; }
        public List<ActiveType> ActiveTypes { get; set; }
        public bool IsNew { get; set; }
    }
    public class ActiveType
    {
        public string Id { get; set; }
        public string TypeName { get; set; }
        public int MaxVol { get; set; }
        public override string ToString() => TypeName;

    }
    public class GamerActive 
    {
        public string ActiveName { get; set; }
        //public string ActiveID { get; set; }
        public string Id { get; set; }
        public string Category { get; set; }
        public int Volume { get; set; }
        public int IsCurrency { get; set; }
        public string IsCurrencyStr => (IsCurrency == 1) ? "Да" : "Нет";
        public decimal Cost { get; set; }
        public string CostStr => Cost.ToString("F2");
        public decimal Max { get; set; }
    }
    public class ExchangeEntity
    {
        public string CurrId { get; set; }
        public string CurrName { get; set; }
        public string GameName { get; set; }
        public decimal Rate { get; set; }
        public int Volume { get; set; } 
        public int MaxVolume { get; set; }
        public decimal Komiss { get; set; }
        public int MinKomiss { get; set; }
        public int FullKomiss { get; set; }

    }
    public class ExchangeOrder
    {
        public string GameSellId { get; set; }
        public string GameBuyId { get; set; }
        public string SellCurrId { get; set; }
        public string BuyCurrId { get; set; }
        public int SellVolume { get; set; }
        public int BuyVolume { get; set; }
    }
    public class Tournament
    {
        private DateTime _start, _endOfSubscribe;
        public string Id { get; set; }
        public string UserId { get; set; } // ID текущего игрока
        public virtual string Name { get; set; }
        public List<CompApplicant> Subscribed { get; set; }
        public DateTime EndOfSubscribe { get => _endOfSubscribe.ToLocalTime(); set => _endOfSubscribe=value; }
        public DateTime Start { get => _start.ToLocalTime(); set => _start = value; }
        public int Ante { get; set; }
        public int Cenz { get; set; }
        public int AuthorGift { get; set; }
        public int RoundsNum => Rounds.Count;
        public int MembersInRound { get; set; }
        public int AllMembers => (int)Math.Pow(MembersInRound, RoundsNum);
        public int SubscribedMembers { get; set; } = 0;
        public List<TournRound> Rounds { get; set; }
        public bool IsSeeded { get; set; }
        public int PlaceFutureCount { get; set; }
        public List<RoundPlace> AllPlaces => (from p in Rounds
                                                   let r = p.RoundPlaces
                                                   from rp in r
                                                   where !rp.Finished 
                                                   select rp).ToList();
        public List<CompApplicant> AllApplicants => (from p in AllPlaces
                                                     let a = p.Applicants
                                                     from aa in a
                                                     select aa).ToList();

        public TournRound CurrentRound => 
            MyPlace == null ? null : (from p in Rounds 
                                                                    let r = p.RoundPlaces
                                                                    from rp in r
                                                                    let a = rp.Applicants
                                                                    from aa in a
                                                                    where rp.Id == MyPlace.Id && !rp.Finished && aa.Id == UserId
                                                                    select p).SingleOrDefault();
        public RoundPlace MyPlace =>
            (from ap in AllPlaces let p = ap.Applicants from m in p where m.Id  == UserId && !ap.Finished select ap).SingleOrDefault(); //  && !ap.IsStarted
        public bool Loosed(string userconnectid)
        { 
            var subs = Subscribed.Any((a) => a.ConnectId == userconnectid);
            var apl = (from p in Rounds
                       let r = p.RoundPlaces
                       from rp in r
                       let a = rp.Applicants
                       from aa in a
                       where aa.Id == UserId
                       select rp);
            var dfd = apl.Where((z) => !z.Finished).FirstOrDefault();
            var ftf = apl.FirstOrDefault();
            var res = !subs && dfd == null && ftf != null; 
            return res; 
        }      
    }

    public class CompApplicant
    {
        public string Id { get; set; }
        public string ConnectId { get; set; }
        public string Name { get; set; }
        public int Score { get; set; }
        public int Rating { get; set; }
        public int Capital { get; set; }

        public string PlaceId { get; set; }
        public string InPlaceId { get; set; }
        public string NextPlaceId { get; set; }
        public bool IsOnline => !string.IsNullOrEmpty(ConnectId); 
        public bool IsSubscribed { get; set; }
        public bool Started { get; set; }
        public string CustomParams { get; set; }
        public string CompId { get; set; }
    }
    public class CompSeededApps
    {
        public string CompId { get; set; }
        public List<CompApplicant> Aplicants { get; set; }
    }
    public class TournRound
    {
        public int RoundNum { get; set; }
        public string RoundName
        {
            get
            {
                if (RoundNum == 1) return "Финал";
                if (RoundNum == 2) return "Полуфинал";
                if (RoundNum == 3) return "Четвертьфинал";
                return $"1/{((int)Math.Pow(2, RoundNum - 1)).ToString()} финала";
            }
        }
        public IEnumerable<RoundParameter> Parameters { get; set; }
        public IEnumerable<CompApplicant> TourApplicants { get; set; }
        public IEnumerable<RoundPlace> RoundPlaces { get; set; }

    }
    public class RoundPlace
    {
        public string Id { get; set; }
        private DateTime? LStart;
        public DateTime? Start { get => LStart.HasValue ? (DateTime?)LStart.Value.ToLocalTime() : null; set => LStart = value; }
        public int CurrentFrame { get; set; }
        public List<CompApplicant> Applicants { get; set; }
        public bool Finished { get; set; }
        public int FuturePartners { get; set; }
        public bool PathToUpExists { get; set; }
        //public bool IsStarted { get; set; }
    }
    public class NextRoundPlace
    {
        public int RoundNum { get; set; }
        public string FromPlaceId { get; set; }
        public string ToPlaceID { get; set; }
        public string GamerConnectId { get; set; }
        public string RoundName
        {
            get
            {
                if (RoundNum == 1) return "Финал";
                if (RoundNum == 2) return "Полуфинал";
                if (RoundNum == 3) return "Четвертьфинал";
                return $"1/{((int)Math.Pow(2, RoundNum - 1)).ToString()} финала";
            }
        }
        public int FuturePartners { get; set; }
        public bool PathToUpExists { get; set; }
    }

    public class Champ
    {
        private DateTime _start, _endOfSubscribe;
        public string Id { get; set; } // из базы
        public string UserId { get; set; }
        public string Name { get; set; }  // из базы
        public List<CompApplicant> Subscribed { get; set; } // Все подписавшиеся, не посеянные
        public DateTime EndOfSubscribe { get => _endOfSubscribe.ToLocalTime(); set => _endOfSubscribe = value; }  // из базы
        public DateTime Start { get => _start.ToLocalTime(); set => _start = value; } // из базы
        public int Ante { get; set; } // из базы
        public int Cenz { get; set; } // из базы
        public int AuthorGift { get; set; }  // из базы
        public int RoundsNum => (int)Math.Pow(NumberOfMembers, 2) - NumberOfMembers;
        public int NumberOfMembers { get; set; }
        public List<ChampPlace> Places { get; set; }  // из базы
        public int Seeded { get; set; } // турнир посеян
        public bool IsUserInComp { get; set; }
        public bool IsSeeded => Seeded == 1;
        public bool Finished(string userconnectid) => Seeded == 1 && (from p in Places
                                                      let a = p.Applicants
                                                      from aa in a
                                                      where aa.ConnectId == userconnectid
                                                                      select p).All(z => z.Ended.HasValue);
        public List<CompParameter> Parameters { get; set; }
    }
    public class ChampPlace
    {
        private DateTime? _started, _ended;
        public int LPlace { get; set; }
        public int RPlace { get; set; }
        public string PLaceId { get; set; }
        public DateTime? Started { get => _started.HasValue? (DateTime?)_started.Value.ToLocalTime():null; set => _started = value ; }
        public DateTime? Ended { get => _ended.HasValue? (DateTime?)_ended.Value.ToLocalTime() : null; set => _ended = value; }
        public List<CompApplicant> Applicants { get; set; }
        public int FrameNum { get; set; }

    }

    public class Anterior
    {
        private DateTime _start, _end;
        public string Id { get; set; } // из базы
        public string Name { get; set; }  // из базы
        public List<AnterApplicant> Applicants { get; set; } // Все Сыгравшие
        public DateTime End { get => _end.ToLocalTime(); set => _end = value; }  // из базы
        public DateTime Start { get => _start.ToLocalTime(); set => _start = value; } // из базы
        public int Ante { get; set; } // из базы
        public int Cenz { get; set; } // из базы
        public int AuthorGift { get; set; }  // из базы
        public int NumberOfMembers { get; set; }
        public List<CompParameter> Parameters { get; set; }
        public string PlaceId { get; set; }
    }
    public class AnterApplicant
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Rating { get; set; }
        public int Capital { get; set; }
        public string PlaceId { get; set; }
        public string CompResult { get; set; }
    }
    public class Frame
    {
        public string CompId { get; set; }
        public string PlaceId { get; set; }
        public string UserConnectId { get; set; }
        public int FrameNum { get; set; }
        public int Score { get; set; }
    }
    public class FrameParam
    {
        public string Id { get; set; }
        public string MatchId { get; set; }
        public string Value { get; set; }
        public int FrameNum { get; set; }
    }

    public class RoundParameter : CompParameter
    {
        public string RoundId { get; set; }

        public int RoundNum { get; set; }
    }
    public class CompParameter
    {
        public string Id { get; set; }
        public string ParamName { get; set; }
        public int Value { get; set; }
    }

    public class RoundStart
    {
        private DateTime? _start;
        public string Pid { get; set; }
        public DateTime? Start { get => _start.HasValue?(DateTime?)_start.Value.ToLocalTime():null; set => _start = value; }
        public bool SendEmail { get; set; }
    }

    public class FrameResult
    {
        public string CompId { set; get; }
        public string UserId { get; set; }
        public string MatchId { set; get; }
        public int FrameNum { get; set; }
        public string Results { get; set; }
        public int Points { get; set; }
        public int WinFrames { get; set; }
        public int LoosFrames { get; set; }
        public DateTime? Ended { get; set; }
    }
    public class ChampPref
    {
        public string CompId { get; set; }
        public string UserId { get; set; }
        public int PlaceNum { get; set; }
        public int Prize { get; set; }
        public int Rate { get; set; }
        public decimal PrefVolume { get; set; }
    }

    public class AnterResult : ChampPref
    {        public decimal Score { get; set; }
    }

    public class ChampGamerResult
    {
        public string GamerID { get; set; }
        public int Points { get; set; }
        public int LF { get; set; }
        public int RF { get; set; }
    }
    public class ChampMeetingScore
    {
        public string FirstUser { get; set; }
        public string SecondUser { get; set; }
        public int FirstUserScore { get; set; }
        public int SecondUserScore { get; set; }
        public int FirstRating { get; set; }
        public int SecondRating { get; set; }
    }
    public class MatchResults
    {
        public string MatchId { set; get; }
        public string WinnerId { set; get; }
        public List<MatchResult> Results { set; get; }
    }
    public class MatchResult
    {
        public string MatchId { set; get; }
        public decimal Result { set; get; }
        public decimal Active { set; get; }
        public string Comment { get; set; }

    }

    public class FreeMatch
    {
        private DateTime? _dateStarted;
        public string Creator { get; set; }
        public string CreatorID { get; set; }
        public string CompId { get; set; } = "";
        public List<CompApplicant> Participants { get; set; }
        public List<GameParameter> Parameters { get; set; }
        public string JsonParam { get; set; }
        public int Partners { get; set; } = 2;
        public int Frames { get; set; } = 1;
        public Dictionary<string, GameParameter> ParamsDict => Parameters.ToDictionary(p => p.Id);
        public int FrameNum { get; set; }
        public DateTime? DateStarted { get => _dateStarted.HasValue? (DateTime ? )_dateStarted.Value.ToLocalTime():null; set => _dateStarted = value; }
        public List<GameParameter> NewParams { get; set; }
    }
    public class NewFreeMatch
    {
        public List<NewGameParameter> Parameters { get; set; }
        public string JsonParam { get; set; }
        public int Partners { get; set; } = 2;
        public int Frames { get; set; } = 1;
    }
    public class NewGameParameter
    {
        public string Id { get; set; }
        public string CompId { get; set; }
        public string ParamName { get; set; }
        public int Value { get; set; }
    }

    public class GameParameter
    {
        public string Id { get; set; }
        public string CompId { get; set; }
        public string ParamName { get; set; }
        public int DefValue { get; set; }
    }

    public enum ERROR
    {

        NOERROR = 0,
        USERNOTEXISTS,
        WRONGPASSWORD,
        USERNAMEEXISTS,
        USEREMAILEXISTS,
        USERNOTINGAME,
        USERINGAMELOCKED,
        GAMELOCKED,
        USERINGAME,
        WRONGREQUEST,
        COMMONERROR,
        WRONGEMAILFORMAT,
        GAMENOTEXISTS,
        USERLOCKED,
        UNAUTHORIZED,
        DOUBLEENTERPERMITS,
        GAMELOCKEDBYAUTHOR,
        GAMEADMINLOCKED,
        NETERROR,
        WRONGARGUMENTS,
        PASSWORDINVALID,

    }
    public static class ErrorMess
    {
        public static Dictionary<ERROR, string> Messages = new Dictionary<ERROR, string>()
        {
            { ERROR.NOERROR, "OK"},
            { ERROR.USERNOTEXISTS, "Пользователь не существует"},
            { ERROR.WRONGPASSWORD, "Неправильный пароль"},
            { ERROR.USERNAMEEXISTS, "Пользователь с этим именем уже существует"},
            { ERROR.USEREMAILEXISTS, "Этот e-mail уже зарегистрирован"},
            { ERROR.USERNOTINGAME, "Пользователь не подключен к игре"},
            { ERROR.USERINGAMELOCKED, "Пользователь заблокирован в игре"},
            { ERROR.GAMELOCKED, "Игра заблокирована в системе"},
            { ERROR.USERINGAME, "Пользователь уже в игре"},
            { ERROR.UNAUTHORIZED, "Неавторизованный вход"},
            { ERROR.WRONGARGUMENTS, "Неправильные параметры"},
            { ERROR.WRONGEMAILFORMAT, "Неверный формат e-mail"},
            { ERROR.GAMENOTEXISTS, "Игра не существует"},
            { ERROR.COMMONERROR, "Общая ошибка"},
            { ERROR.USERLOCKED, "Пользователь заблокирован"},
            { ERROR.PASSWORDINVALID, "Пароль должен иметь минимум 6 символов"},
            { ERROR.NETERROR, "Server error"},
            { ERROR.DOUBLEENTERPERMITS, "Множественные входы в игру запрещены"},
            { ERROR.GAMELOCKEDBYAUTHOR, "Игра заблокирована автором"},
       };
    }
    public class ChatMess
    {
        public int? Dir
        {
            get;
            set;
        }
        public string MyMessage { private get; set; }
        public string HisMessage { private get; set; }
        public string MessageId { get; set; }
        public string senderid { get; set; }
        public string recipientid { get; set; }
        public DateTimeOffset MessageDateTime { private get; set; }
        public bool IsMyMess => (Dir == 0);

        public string Message { get; set; }
        public string MessDate
        {
            get
            {
                return MessageDateTime.ToString("dd/MM/yy HH:mm");
            }
        }

    }
    public class FreeMesssage
    {
        public string FromId { get; set; }
        public string MethodJsonParams { get; set; }
    }
    public enum UserInCompState
    {
        UNDEFINED = -1,
        CHOOSING,
        INCOMP,
        FINISHED
    }
    public enum COMPTYPE
    {
        CHAMP = 0,
        TOURNIR,
        FREEMATHCH,
        ANTERIORITY
    }
}
