﻿using System;
using System.Collections.Generic;

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
        public string DeviceId { get; set; }
    }
    public class Interlocutor
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string SrConnectID { get; set; }
        public int MessCount { get; set; }
    }
    public class UserRegister : UserLogin
    {
        public string Email { get; set; }
    }
    public class SsfActionResult 
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
        public string UserId { get; set; }
        public int IsCurrency { get; set; }
    }
    public class ExchangeOffer
    {
        public string OfferId { get; set; }
        public string OfferNum { get; set; }
        public string SellerId { get; set; }
        public string SellerName { get; set; }
        public decimal FullCost { get; set; }
        public DateTime? CreateDate { get; set; }
        public bool IsNew { get; set; } = false;
        public string Hash { get; set; } = string.Empty;
        public List<ActiveType> ActiveTypes { get; set; }
        public List<ExchangeOfferGood> Goods { get; set; }
    }
    public class ExchangeOfferGood
    {
        public string GoodId { get; set; }
        public string ActiveId { get; set; }
        public string ActiveName { get; set; }
        public int Volume { get; set; }
        public int MaxVol { get; set; }
        public decimal Cost { get; set; }
        public bool IsNew { get; set; }
        public decimal FullCost => Volume * Cost;
        public string OfferId { get; set; }
    }
    public class ExchangeUpdateResult
    {
        public ExchangeOffer Offer { get; set; }
        public int ChangeState { get; set; }
    }
    public class ActiveType
    {
        public string Id { get; set; }
        public string TypeName { get; set; }
        public int MaxVol { get; set; }
        public override string ToString() => TypeName;
    }
    public class StringAndInt
    {
        public string S { get; set; }
        public string S1 { get; set; }
        public int I { get; set; }
        public int I1 { get; set; }
    }
    public class ChampPlaceScore
    {
        public string PlaceId { get; set; }
        public List<GamerPoints> GamerPoints { get; set; }
        public List<Order> Rateord { get; set; }
    } 
    public class GamerPoints
    {
        public string UserId { get; set; }
        public int Points { get; set; }
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
    public class ChangeRatingRequest
    {
        public string Uid { get; set; }
        public int Value { get; set; }
        public string Comment { get; set; }
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
        public int FullKomiss => (int)Math.Round(Volume * Komiss * 0.01M);
    }
    public class ExchangeResult
    {
        public bool Success { get; set; }
        public List<ExchangeEntity> Entityes { get; set; }
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
    public class AnterApplicant : CompApplicant
    {
        public List<string> CompResult { get; set; }
        public int FrameNum { get; set; }
    }
    public class TournirApplicant : CompApplicant
    {
        public List<string> CompResult { get; set; }
        public int FrameNum { get; set; }
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
        public bool Started => InCompState == UserInCompState.INFRAME;
        public string CustomParams { get; set; }
        public string CompId { get; set; }
        public UserInCompState InCompState { get; set; } = UserInCompState.UNDEFINED;
    }
    public enum UserInCompState
    {
        UNDEFINED = -1,
        FINISHED,
        READY,
        INFRAME,
        WAITFOREOF,
        WAITFORSTART
    }
    public class CompApplicantDistinctComparer : IEqualityComparer<CompApplicant>
    {
        public bool Equals(CompApplicant x, CompApplicant y) => x.Id == y.Id;
        public int GetHashCode(CompApplicant obj) => obj.Id.GetHashCode();
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
        public IEnumerable<CompParameter> Parameters { get; set; }
        public IEnumerable<RoundPlace> RoundPlaces { get; set; }
        public override string ToString() => RoundName;
    }
    public class GamerAchivment
    {
        public string CompName { get; set; }
        public DateTime Start { get; set; }
        public string Achivment { get; set; }
        public int CompType { get; set; }
    }
    public class ChampFrameResult
    {
        public string FirstUserid { get; set; }
        public string SecondUserid { get; set; }
        public string FirstUserResult { get; set; }
        public string SecondUserResult { get; set; }
        public int FirstUserScore { get; set; }
        public int SecondUserScore { get; set; }
        public int FrameNum { get; set; }
        public string MatchId { get; set; }
        public UserInCompState NewState { get; set; }
    }
    public class Champ : Competition
    {
        public DateTime End { get; set; }
        public List<CompPlace> Places { get; set; }  // из базы
        public List<CompParameter> Parameters { get; set; }
    }
    public class Tournament : Competition
    {
        private DateTime endOfsubscribe;
        public List<CompApplicant> Subscribed { get; set; }
        public DateTime EndOfSubscribe { get {return endOfsubscribe.ToLocalTime(); } set { endOfsubscribe = value; } }
        public int RoundsNum => Rounds.Count;
        public int MembersInRound { get; set; }
        override public int NumberOfMembers => (int)Math.Pow(MembersInRound, RoundsNum);
        public List<TournRound> Rounds { get; set; }
        public bool IsSeeded { get; set; }
        public int PlaceFutureCount { get; set; }
    }
    public class Competition
    {
        private DateTime start;
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime Start { get { return start.ToLocalTime(); } set { start = value; } }  //
        public int Ante { get; set; } // из базы
        public int Cenz { get; set; } // из базы
        public int AuthorGift { get; set; }
        public virtual int NumberOfMembers { get; set; }
    }
    public class Anterior : Competition
    {
        public List<AnterApplicant> Applicants { get; set; }
        public DateTime End { get; set; }
        public List<CompParameter> Parameters { get; set; }
    }
    public class FreeMatchState
    {
        public bool IsFinished { get; set; }
        public int CurrentFrameNum { get; set; }
    }
    public class FreeMatch
    {
        private DateTime? _dateStarted;
        public string Creator { get; set; }
        public string CreatorID { get; set; }
        public string WinnerGamerId { get; set; }
        public string CompId { get; set; } = "";
        public List<CompApplicant> Participants { get; set; }
        public List<CompParameter> Parameters { get; set; }
        public string JsonParam { get; set; }
        public int Partners { get; set; } = 2;
        public int Frames { get; set; } = 1;
        public int FrameNum { get; set; }
        public DateTime? DateStarted { get => _dateStarted.HasValue ? (DateTime?)_dateStarted.Value.ToLocalTime() : null; set => _dateStarted = value; }
    }
    public class CompPlace
    {
        private DateTime? _start, _end;
        public string Id { get; set; }
        public List<CompApplicant> Applicants { get; set; }
        public DateTime? Start { get { return _start.HasValue?_start.Value.ToLocalTime(): _start; }
            set { _start = value; } }
        public DateTime? End { get { return _end.HasValue ? _end.Value.ToLocalTime() : _end; } set { _end = value; } }
        public int FrameNum { get; set; }
    }
    public class RoundPlace : CompPlace
    {
        public string FromPlaceId { get; set; }
        public bool Finished { get; set; }
        public bool PathToUpExists { get; set; }
        public List<GamerFrameResult> TournirPlaceResults { get; set; }
    }
    public class GamerFrameResult
    {
        public int FrameNum { get; set; }
        public string UserId { get; set; }
        public string Results { get; set; }
    }
    public class NextRoundPlace 
    {
        public string FromPlaceId { get; set; }
        public int RoundNum { get; set; }
        public string ToPlaceID { get; set; }
        public string GamerId { get; set; }
        public int FuturePartners { get; set; }
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
        public bool PathToUpExists { get; set; }
    }
    public class FrameParam
    {
        public string Id { get; set; }
        public string MatchId { get; set; }
        public string UserId { get; set; }
        public string Value { get; set; }
        public int FrameNum { get; set; }
        public DateTime DateSetted { get; set; }
    }
    public class RoundStart
    {
        public string Pid { get; set; }
        public DateTime? Start { get; set; } 
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
    }
    public class AnterResult : ChampPref
    {
        public decimal Score { get; set; }
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
  
    }
    public class FreeMatchScore
    {
        public string CompId { get; set; }
        public List<GamerPoints> GamerPoints { get; set; }
    }
    public class FreeMatchResult
    {
        public MatchResults FinalMatchResults { get; set; }
        public FreeMatchScore MatchScore { get; set; }
    }
    public class CompParameter
    {
        public string Id { get; set; }
        public string ParamName { get; set; }
        public int Value { get; set; }
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
        ADMINMONEYSNOTENOUGH,
        WRONGUSERNAME,
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
            { ERROR.WRONGUSERNAME, "Ошибка в имени. Допустимы только цифры, латиница и кириллица"}
        };
    }
    public class Chat
    {
        public int Dir { get; set; }
        public string MessageId { get; set; }
        public string senderid { get; set; }
        public DateTime MessageDateTime { get; set; }
        public bool IsMyMess => (Dir == 0);
        public string Message { get; set; }

    }
    public class FreeMesssage
    {
        public string FromId { get; set; }
        public string MethodJsonParams { get; set; }
    }
    public class OfferState
    {
        public string State { get; set; }
        public string OfferId { get; set; }
        public string Hash { get; set; }
    }
    public enum COMPTYPE
    {
        CHAMP = 0,
        TOURNIR,
        FREEMATHCH,
        ANTERIORITY
    }
}

