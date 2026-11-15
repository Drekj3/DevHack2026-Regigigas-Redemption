using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
namespace SecondChance
{
    // Bot skill per Valorant rank tier, 0 Iron ... 8 Radiant. Numbers scale smoothly; habits switch on at a tier.
    // ponytail: hand-tuned curves, not fitted to real rank data; adjust the Lerp endpoints after playtesting.
    public struct RankProfile
    {
        public static readonly string[] Names={"IRON","BRONZE","SILVER","GOLD","PLATINUM","DIAMOND","ASCENDANT","IMMORTAL","RADIANT"};
        public int rank;
        public float reaction,accuracy,headshot,fireInterval,turnSpeed,hearing,abilityCooldown;
        public bool rushes,usesCover,strafes,preaims,flash,wall,ult;
        public int reads; // times a player habit must show before the bot acts on it (PlayerRead)
        public string Name=>Names[rank];
        // edge: sparring pressure in rank steps (PlayerRead.Edge). It moves the mechanics, never the habits.
        public static RankProfile For(int rank,float edge=0)
        {
            rank=Mathf.Clamp(rank,0,Names.Length-1);float t=Mathf.Clamp01((rank+edge)/(Names.Length-1f));
            return new RankProfile{rank=rank,reads=rank<=1?3:rank<=3?2:1, // Iron/Bronze need it three times, Silver/Gold twice, Platinum+ once
                reaction=Mathf.Lerp(.75f,.17f,t),accuracy=Mathf.Lerp(.22f,.95f,t),headshot=Mathf.Lerp(.03f,.45f,t),
                fireInterval=Mathf.Lerp(.5f,.22f,t),turnSpeed=Mathf.Lerp(170f,450f,t),hearing=Mathf.Lerp(6f,18f,t),
                abilityCooldown=Mathf.Lerp(11f,2.5f,t),
                rushes=rank<=1,      // Iron/Bronze: run straight at you, never take cover
                usesCover=rank>=2,   // Silver+: back off when low or reloading
                strafes=rank>=3,     // Gold+: counter-strafe in fights
                preaims=rank>=3,     // Gold+: pre-aim the last known spot
                flash=rank>=2,wall=rank>=3,ult=rank>=4};
        }
        public string Summary=>"Reaction "+reaction.ToString("0.00")+"s  /  Aim "+Mathf.RoundToInt(accuracy*100)+"%  /  Head "+Mathf.RoundToInt(headshot*100)+"%\n"+
            (rushes?"Rushes, no cover":strafes?"Strafes, pre-aims, uses cover":"Uses cover")+"  /  Utility: molly"+(flash?", flash":"")+(wall?", wall":"")+(ult?", ult":"");
    }

    // Riot ID -> bot rank via the Riot API: Valorant competitive tier when the key allows it
    // (production keys only; development keys get 403), otherwise the same account's League of Legends rank.
    public static class RiotRank
    {
        // Never in source control: RIOT_API_KEY env var, else UserSettings/RiotApiKey.txt (gitignored by Unity's template).
        public static string Key()
        {
            string k=Environment.GetEnvironmentVariable("RIOT_API_KEY");
            if(!string.IsNullOrWhiteSpace(k))return k.Trim();
            try{string f=Path.Combine(Application.dataPath,"..","UserSettings","RiotApiKey.txt");if(File.Exists(f))return File.ReadAllText(f).Trim();}catch(Exception){}
            return "";
        }
        [Serializable] class Account{public string puuid,gameName,tagLine;}
        [Serializable] class Shard{public string activeShard;}
        [Serializable] class MatchList{public Entry[] history;}
        [Serializable] class Entry{public string matchId,queueId;public long gameStartTimeMillis;}
        [Serializable] class Match{public Player[] players;}
        [Serializable] class Player{public string puuid;public int competitiveTier;}

        // done(status message, rank 0..8 or -1 when the rank couldn't be read).
        public static IEnumerator Lookup(string riotId,Action<string,int> done)
        {
            string key=Key();
            if(key==""){done("No Riot API key. Set RIOT_API_KEY or put it in UserSettings/RiotApiKey.txt.",-1);yield break;}
            int hash=riotId==null?-1:riotId.LastIndexOf('#');
            if(hash<1||hash==riotId.Length-1){done("Enter a Riot ID like Name#TAG.",-1);yield break;}
            string name=riotId.Substring(0,hash).Trim(),tag=riotId.Substring(hash+1).Trim();
            long code=0;string body="";
            IEnumerator Get(string url){using(var r=UnityWebRequest.Get(url)){r.SetRequestHeader("X-Riot-Token",key);r.timeout=10;yield return r.SendWebRequest();code=r.responseCode;body=r.downloadHandler.text;}}

            yield return Get("https://americas.api.riotgames.com/riot/account/v1/accounts/by-riot-id/"+Uri.EscapeDataString(name)+"/"+Uri.EscapeDataString(tag));
            if(code==404){done("Riot ID "+name+"#"+tag+" not found.",-1);yield break;}
            if(code==401||code==403){done("Riot API key rejected (development keys expire every 24h). Pick a rank below.",-1);yield break;}
            if(code!=200){done("Riot API error "+code+". Pick a rank below.",-1);yield break;}
            var account=JsonUtility.FromJson<Account>(body);
            string who=account.gameName+"#"+account.tagLine;

            yield return Get("https://americas.api.riotgames.com/riot/account/v1/active-shards/by-game/val/by-puuid/"+account.puuid);
            string shard=code==200?JsonUtility.FromJson<Shard>(body).activeShard:"na";
            if(string.IsNullOrEmpty(shard))shard="na";

            // 1) Valorant competitive tier from the newest ranked games (production keys only).
            yield return Get("https://"+shard+".api.riotgames.com/val/match/v1/matchlists/by-puuid/"+account.puuid);
            string valNote=code==403?"Valorant rank is locked to production keys":code==200?"no rated Valorant games":"Valorant API error "+code;
            if(code==200)
            {
                var history=JsonUtility.FromJson<MatchList>(body).history??new Entry[0];
                Array.Sort(history,(x,y)=>y.gameStartTimeMillis.CompareTo(x.gameStartTimeMillis));
                int checkedMatches=0;
                foreach(var e in history)
                {
                    if(e.queueId!="competitive")continue;
                    if(++checkedMatches>3)break; // ponytail: newest 3 ranked games; tier 0 means placement/unrated
                    yield return Get("https://"+shard+".api.riotgames.com/val/match/v1/matches/"+e.matchId);
                    if(code!=200)continue;
                    foreach(var pl in JsonUtility.FromJson<Match>(body).players??new Player[0])
                        if(pl.puuid==account.puuid&&pl.competitiveTier>=3)
                        {
                            int rank=TierToRank(pl.competitiveTier);
                            done(who+" is "+RankProfile.Names[rank]+" in Valorant. Bot set to match.",rank);yield break;
                        }
                }
            }
            // 2) Same Riot account's League of Legends rank, which every key can read. Servers near the VAL shard first.
            foreach(string platform in Platforms(shard))
            {
                yield return Get("https://"+platform+".api.riotgames.com/lol/league/v4/entries/by-puuid/"+account.puuid);
                if(code!=200||body.Trim()=="[]")continue;
                LeagueEntry best=null;
                foreach(var e in JsonUtility.FromJson<LeagueList>("{\"e\":"+body+"}").e??new LeagueEntry[0])
                    if(e.queueType=="RANKED_SOLO_5x5"||e.queueType=="RANKED_FLEX_SR"&&best==null)best=e;
                if(best==null)continue;
                int rank=LolTierToRank(best.tier);
                done(who+" is "+best.tier+" "+best.rank+" in League ("+platform.ToUpper()+"); "+valNote+". Bot plays at "+RankProfile.Names[rank]+".",rank);yield break;
            }
            done("Found "+who+", but "+valNote+" and no League rank either. Pick a rank below.",-1);
        }
        [Serializable] class LeagueList{public LeagueEntry[] e;}
        [Serializable] class LeagueEntry{public string queueType,tier,rank;}
        static readonly string[] AllPlatforms={"na1","euw1","eun1","kr","br1","la1","la2","oc1","jp1","tr1","ru","me1","sg2","tw2","vn2"};
        static System.Collections.Generic.IEnumerable<string> Platforms(string shard)
        {
            string[] near=shard=="eu"?new[]{"euw1","eun1","tr1","ru","me1"}:shard=="kr"?new[]{"kr"}:shard=="ap"?new[]{"oc1","jp1","sg2","tw2","vn2"}:
                shard=="br"?new[]{"br1"}:shard=="latam"?new[]{"la1","la2"}:new[]{"na1","la1","la2","oc1"};
            foreach(var p in near)yield return p;
            foreach(var p in AllPlatforms)if(Array.IndexOf(near,p)<0)yield return p;
        }
        // League tiers onto the 9 bot ranks: Emerald sits between Plat and Diamond, so it maps to Diamond and the rest shift up.
        public static int LolTierToRank(string tier)
        {
            switch(tier){case "IRON":return 0;case "BRONZE":return 1;case "SILVER":return 2;case "GOLD":return 3;case "PLATINUM":return 4;
                case "EMERALD":return 5;case "DIAMOND":return 6;case "MASTER":case "GRANDMASTER":return 7;case "CHALLENGER":return 8;default:return 3;}
        }
        // Riot competitiveTier: 3-5 Iron, 6-8 Bronze ... 24-26 Immortal, 27 Radiant.
        public static int TierToRank(int tier)=>Mathf.Clamp((tier-3)/3,0,RankProfile.Names.Length-1);
    }
}
