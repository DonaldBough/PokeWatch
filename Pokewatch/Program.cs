﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Google.Protobuf.Collections;
using Pokewatch.Datatypes;
using Pokewatch.DataTypes;
using POGOLib.Net;
using POGOLib.Net.Authentication;
using POGOLib.Pokemon.Data;
using POGOProtos.Enums;
using POGOProtos.Map;
using POGOProtos.Map.Pokemon;
using Tweetinvi.Core.Extensions;
using Location = Pokewatch.Datatypes.Location;

namespace Pokewatch
{
    public class Program
	{
        private static Configuration s_config;
        private static GroupMeBot groupMeBot;
        private static Session s_pogoSession;

        public static void Main(string[] args)
		{
            const string JASPER_GROUPMEBOT_KEY = "1a8604e8049c839ad76a861c4e";
            const string JET_GROUPMEBOT_KEY = "9bcb8b42c2fa8e22b073cb0dc7";

            try
			{

                string json = File.ReadAllText("Configuration_JET.json");
				s_config = new JavaScriptSerializer().Deserialize<Configuration>(json);
			}
			catch(Exception ex)
			{
				Log("[-]Unable to load config.");
				Log(ex.Message);
				return;
			}

			if ((s_config.PTCUsername.IsNullOrEmpty() || s_config.PTCPassword.IsNullOrEmpty()) && (s_config.GAPassword.IsNullOrEmpty() || s_config.GAUsername.IsNullOrEmpty()))
			{
				Log("[-]Username and password must be supplied for either PTC or Google.");
				return;
			}

            //Setup GroupMeBot
            try
            {
                groupMeBot = new GroupMeBot(JET_GROUPMEBOT_KEY); //JET area
            }
            catch (Exception e)
            {
                Log("[-] Problem registrering GroupMe Bot");
            }

            Log("[+]Sucessfully connected GroupMe Bot.");
			if (PreparePOGOClient())
			{
				Log("[+]Sucessfully signed in to PokemonGo, beginning search.");
			};

			if (!Search())
				throw new Exception();
		}

		private static bool Search()
		{
			Queue<FoundPokemon> groupMePokemon = new Queue<FoundPokemon>();
            List<Location> townLocationsList = new List<Location>();
            DateTime lastGroupMeMessage = DateTime.MinValue;
            Random random = new Random();
			while (true)
			{
                int regionIndex = random.Next(s_config.Regions.Count);
                if (regionIndex == s_config.Regions.Count)
                    regionIndex = 0;

                Region region = s_config.Regions[regionIndex];
                Log($"[!]Searching Region: {region.Name}\n");

                foreach (Location location in region.Locations)
				{
					SetLocation(location);

					//Wait so we don't clobber api and to let the heartbeat catch up to our new location. (Minimum heartbeat time is 4000ms)
					Thread.Sleep(5000);
					Log("[!]Searching nearby cells.\n");
					RepeatedField<MapCell> mapCells;
					try
					{
						mapCells = s_pogoSession.Map.Cells;
					}
					catch
					{
						Log("[-]Heartbeat has failed. Terminating Connection.");
						return false;
					}
					foreach (var mapCell in mapCells)
					{
						foreach (WildPokemon pokemon in mapCell.WildPokemons)
						{
							FoundPokemon foundPokemon = ProcessPokemon(pokemon, groupMePokemon, lastGroupMeMessage);

							if (foundPokemon == null)
								continue;
                            
                            string groupMeMessage = ComposeGroupMeMessage(foundPokemon, region);

                            try
							{
                                groupMeBot.PostMessage(groupMeMessage);
							}
							catch(Exception ex)
							{
								Log("[-]groupMeMessage failed to publish: " + groupMeMessage + " " + ex.Message);
								continue;
							}

							Log("[+]groupMeMessage published: " + groupMeMessage);
                            lastGroupMeMessage = DateTime.Now;

							groupMePokemon.Enqueue(foundPokemon);
							if (groupMePokemon.Count > 10)
								groupMePokemon.Dequeue();
						}
					}
				}
				Log("[!]Finished Searching " + region.Name + "\n");
			}
		}
        
        //Sign in to PokemonGO
        private static bool PreparePOGOClient()
		{
			Location defaultLocation;
			try
			{
				defaultLocation = s_config.Regions.First().Locations.First();
			}
			catch
			{
				Log("[-]No locations have been supplied.");
				return false;
			}
			if (!s_config.PTCUsername.IsNullOrEmpty() && !s_config.PTCPassword.IsNullOrEmpty())
			{
				try
				{
					Log("[!]Attempting to sign in to PokemonGo using PTC.");
					s_pogoSession = Login.GetSession(s_config.PTCUsername, s_config.PTCPassword, LoginProvider.PokemonTrainerClub, defaultLocation.Latitude, defaultLocation.Longitude);
					Log("[+]Sucessfully logged in to PokemonGo using PTC.");
					return true;
				}
				catch
				{
					Log("[-]Unable to log in using PTC.");
				}
			}
			if (!s_config.GAUsername.IsNullOrEmpty() && !s_config.GAPassword.IsNullOrEmpty())
			{
				try
				{
					Log("[!]Attempting to sign in to PokemonGo using Google.");
					s_pogoSession = Login.GetSession(s_config.GAUsername, s_config.GAPassword, LoginProvider.GoogleAuth, defaultLocation.Latitude, defaultLocation.Longitude);
					Log("[+]Sucessfully logged in to PokemonGo using Google.");
					return true;
				}
				catch
				{
					Log("[-]Unable to log in using Google.");
				}
			}
			return false;
		}

		private static void SetLocation(Location location)
		{
			Log($"[!]Setting location to {location.Latitude},{location.Longitude}");
			s_pogoSession.Player.SetCoordinates(location.Latitude, location.Longitude);
		}

		//Evaluate if a pokemon is worth tweeting about.
		private static FoundPokemon ProcessPokemon(WildPokemon pokemon, Queue<FoundPokemon> alreadyFound, DateTime lastTweet)
		{
			FoundPokemon foundPokemon = new FoundPokemon
			{
				Location = new Location { Latitude = pokemon.Latitude, Longitude = pokemon.Longitude},
				Kind = pokemon.PokemonData.PokemonId,
				LifeExpectancy = pokemon.TimeTillHiddenMs / 1000
			};

			if (s_config.ExcludedPokemon.Contains(foundPokemon.Kind))
			{
				Log($"[!]Excluded: {foundPokemon.Kind} ({foundPokemon.LifeExpectancy} seconds): {Math.Round(foundPokemon.Location.Latitude, 6)},{Math.Round(foundPokemon.Location.Longitude, 6)}");
				return null;
			}

			if (foundPokemon.LifeExpectancy < s_config.MinimumLifeExpectancy)
			{
				Log($"[!]Expiring: {foundPokemon.Kind} ({foundPokemon.LifeExpectancy} seconds): {Math.Round(foundPokemon.Location.Latitude, 6)},{Math.Round(foundPokemon.Location.Longitude, 6)}");
				return null;
			}

			if (alreadyFound.Contains(foundPokemon))
			{
				Log($"[!]Duplicate: {foundPokemon.Kind} ({foundPokemon.LifeExpectancy} seconds): {Math.Round(foundPokemon.Location.Latitude, 6)},{Math.Round(foundPokemon.Location.Longitude, 6)}");
				return null;
			}

			if ((lastTweet + TimeSpan.FromSeconds(s_config.RateLimit) > DateTime.Now) && !s_config.PriorityPokemon.Contains(foundPokemon.Kind))
			{
				Log($"[!]Limiting: {foundPokemon.Kind} ({foundPokemon.LifeExpectancy} seconds): {Math.Round(foundPokemon.Location.Latitude, 6)},{Math.Round(foundPokemon.Location.Longitude, 6)}");
				return null;
			}

			Log($"[!]Sending: {foundPokemon.Kind} ({foundPokemon.LifeExpectancy} seconds): {Math.Round(foundPokemon.Location.Latitude, 6)},{Math.Round(foundPokemon.Location.Longitude, 6)}");
			return foundPokemon;
		}

        //Build a tweet with useful information about the pokemon, then cram in as many hashtags as will fit.
        private static string ComposeGroupMeMessage(FoundPokemon pokemon, Region region)
        {
            Log("[!]Composing message");
            string latitude = pokemon.Location.Latitude.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-us"));
            string longitude = pokemon.Location.Longitude.ToString(System.Globalization.CultureInfo.GetCultureInfo("en-us"));
            string mapsLink = $"https://www.google.com/maps/place/{latitude},{longitude}";
            string expiration = DateTime.Now.AddSeconds(pokemon.LifeExpectancy).ToLocalTime().ToShortTimeString();
            string message = "";

            if (s_config.PriorityPokemon.Contains(pokemon.Kind))
            {
                message = string.Format(s_config.PriorityTweet, SpellCheckPokemon(pokemon.Kind), region.Prefix, region.Name, region.Suffix, expiration, mapsLink);
            }
            else
            {
                message = string.Format(s_config.RegularTweet, SpellCheckPokemon(pokemon.Kind), region.Prefix, region.Name, region.Suffix, expiration, mapsLink);
            }

            message = Regex.Replace(message, @"\s\s", @" ");
            message = Regex.Replace(message, @"\s[!]", @"!");

            Log("[!]Sucessfully composed message.");
            return message;
        }

        //Generate user friendly and hashtag friendly pokemon names
        private static string SpellCheckPokemon(PokemonId pokemon, bool isHashtag = false)
		{
			string display;
			switch (pokemon)
			{
				case PokemonId.Farfetchd:
					display = isHashtag ? "Farfetchd" : "Farfetch'd";
					break;
				case PokemonId.MrMime:
					display = isHashtag ? "MrMime" : "Mr. Mime";
					break;
				case PokemonId.NidoranFemale:
					display = isHashtag ? "Nidoran" : "NidoranBoy";
					break;
				case PokemonId.NidoranMale:
					display = isHashtag ? "Nidoran" : "NidoranFemale";
					break;
				default:
					display = pokemon.ToString();
					break;
			}
			if (s_config.PokemonOverrides.Any(po => po.Kind == pokemon))
			{
				display = s_config.PokemonOverrides.First(po => po.Kind == pokemon).Display;
			}
			Regex regex = new Regex("[^a-zA-Z0-9]");
			return isHashtag ? regex.Replace(display, "") : display;
		}

        private static void Log(string message)
		{
			Console.WriteLine(message);
			using (StreamWriter w = File.AppendText("log.txt"))
			{
				w.WriteLine(DateTime.Now + ": " + message);
			}
		}
	}
}
