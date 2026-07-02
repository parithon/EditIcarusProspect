// Copyright 2026 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using IcarusSaveLib;
using Spectre.Console;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using UeSaveGame;
using UeSaveGame.DataTypes;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;

namespace EditIcarusProspect
{
	#region Logger.cs

	/// <summary>
	/// Utility for logging output messages
	/// </summary>
	internal class Logger
	{
		/// <summary>
		/// The minimum level of messages to print. Logged messages below this threshold will be discarded
		/// </summary>
		public LogLevel LogLevel { get; set; }

		public Logger()
		{
#if DEBUG
			LogLevel = LogLevel.Debug;
#else
			LogLevel = LogLevel.Information;
#endif
		}

		/// <summary>
		/// Logs a message at a specific level
		/// </summary>
		public void Log(LogLevel level, string message)
		{
			if (level < LogLevel) return;

			string escaped = Markup.Escape(message);
			string output = level switch
			{
				LogLevel.Warning => $"[yellow][[WARNING]][/] {escaped}",
				LogLevel.Error or LogLevel.Fatal => $"[red][[ERROR]][/] {escaped}",
				LogLevel.Important => $"[white]{escaped}[/]",
				LogLevel.Debug or LogLevel.Verbose => $"[grey]{escaped}[/]",
				_ => escaped
			};

			AnsiConsole.MarkupLine(output);
		}

		/// <summary>
		/// Logs a completely empty line at a specific level
		/// </summary>
		public void LogEmptyLine(LogLevel level)
		{
			if (level < LogLevel) return;
			AnsiConsole.WriteLine();
		}

		public void Render(Table table, LogLevel level = LogLevel.Information)
		{
			if (level < LogLevel) return;
			AnsiConsole.Write(table);
		}

		public void Render(Rule rule, LogLevel level = LogLevel.Information)
		{
			if (level < LogLevel) return;
			AnsiConsole.Write(rule);
		}

		public void Debug(string message) => Log(LogLevel.Debug, message);
		public void Information(string message) => Log(LogLevel.Information, message);
		public void Important(string message) => Log(LogLevel.Important, message);
		public void Warning(string message) => Log(LogLevel.Warning, message);
		public void Error(string message) => Log(LogLevel.Error, message);
	}

	/// <summary>
	/// Logger designed to log to the console
	/// </summary>
	internal sealed class ConsoleLogger : Logger
	{
		public static ConsoleLogger Create(Encoding encoding)
		{
			Console.OutputEncoding = encoding;
			return new ConsoleLogger();
		}
	}

	/// <summary>
	/// Represents the importance of messages being logged
	/// </summary>
	internal enum LogLevel : int
	{
		Verbose,
		Debug,
		Information,
		Important,
		Warning,
		Error,
		Fatal
	}

	#endregion

	#region CharacterReader.cs
	#endregion

	#region CharacterReader.cs

	internal static class CharacterReader
	{
		/// <summary>
		/// Reads data about characters from recorders in a prospect
		/// </summary>
		/// <param name="prospect">The prospect to read</param>
		/// <param name="logger">For logging errors</param>
		/// <param name="sort">Whether to sort the list of characters</param>
		public static CharactersData ReadCharacters(ProspectSave prospect, Logger logger, bool sort = false)
		{
			List<CharacterData> characters = new();
			CharactersData result = new() { Characters = characters };

			HashSet<string> recordersToRead = new(StringComparer.OrdinalIgnoreCase)
			{
				"/Script/Icarus.DynamicRocketSpawnRecorderComponent",
				"/Script/Icarus.GravestoneRecorderComponent",
				"/Script/Icarus.IcarusMountCharacterRecorderComponent",
				"/Script/Icarus.PlayerHistoryRecorderComponent",
				"/Script/Icarus.PlayerRecorderComponent",
				"/Script/Icarus.PlayerStateRecorderComponent",
				"/Script/Icarus.RocketRecorderComponent"
			};

			FProperty[] recorderProperties = (FProperty[])prospect.ProspectData[0].Property!.Value!;

			List<RecorderData> playerRecorders = new();
			ArrayProperty? savedHistoryProperty = null;
			Dictionary<CharacterID, string> characterNameMap = new();
			Dictionary<CharacterID, int> characterHistoryIndexMap = new();
			Dictionary<CharacterID, RecorderData?> playerStateRecorderMap = new();
			Dictionary<CharacterID, List<RecorderData>> playerMountRecorderMap = new();
			Dictionary<int, RecorderData?> rocketSpawnRecorderMap = new();
			Dictionary<int, RecorderData?> rocketRecorderMap = new();
			Dictionary<int, RecorderData?> gravestoneRecorderMap = new();

			void addActorToMap(RecorderData recorder, Dictionary<int, RecorderData?> map)
			{
				foreach (FPropertyTag prop in recorder.Data)
				{
					if (prop.Name.Value.Equals("IcarusActorGUID", StringComparison.OrdinalIgnoreCase))
					{
						int uid = (int)prop.Property!.Value!;
						map.Add(uid, recorder);
					}
				}
			}

			for (int i = 0; i < recorderProperties.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorderProperties[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;

				if (!recordersToRead.Contains(recorderName)) continue;

				IList<FPropertyTag> recorderData = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
				RecorderData recorder = new() { Name = recorderName, Index = i, Data = recorderData };

				if (recorderName.Equals("/Script/Icarus.PlayerRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					playerRecorders.Add(recorder);
				}
				else if (recorderName.Equals("/Script/Icarus.PlayerStateRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					foreach (FPropertyTag prop in recorder.Data)
					{
						if (prop.Name.Value.Equals("PlayerCharacterID", StringComparison.OrdinalIgnoreCase))
						{
							CharacterID? id = ReadCharacterID(prop.Property!);
							if (id.HasValue)
							{
								playerStateRecorderMap.Add(id.Value, recorder);
							}
						}
					}
				}
				else if (recorderName.Equals("/Script/Icarus.IcarusMountCharacterRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					foreach (FPropertyTag prop in recorder.Data)
					{
						if (prop.Name.Value.Equals("OwnerCharacterID", StringComparison.OrdinalIgnoreCase))
						{
							CharacterID? id = ReadCharacterID(prop.Property!);
							if (id.HasValue)
							{
								List<RecorderData>? mountList;
								if (!playerMountRecorderMap.TryGetValue(id.Value, out mountList))
								{
									mountList = new();
									playerMountRecorderMap.Add(id.Value, mountList);
								}
								mountList.Add(recorder);
							}
						}
					}
				}
				else if (recorderName.Equals("/Script/Icarus.DynamicRocketSpawnRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					addActorToMap(recorder, rocketSpawnRecorderMap);
				}
				else if (recorderName.Equals("/Script/Icarus.RocketRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					addActorToMap(recorder, rocketRecorderMap);
				}
				else if (recorderName.Equals("/Script/Icarus.GravestoneRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					addActorToMap(recorder, gravestoneRecorderMap);
				}
				else if (recorderName.Equals("/Script/Icarus.PlayerHistoryRecorderComponent", StringComparison.OrdinalIgnoreCase))
				{
					result.PlayerHistoryRecorder = recorder;

					foreach (FPropertyTag prop in result.PlayerHistoryRecorder.Data)
					{
						if (prop.Name.Value.Equals("SavedHistoryData"))
						{
							savedHistoryProperty = (ArrayProperty)prop.Property!;
							for (int j = 0; j < savedHistoryProperty.Value!.Length; ++j)
							{
								FProperty history = ((FProperty[])savedHistoryProperty.Value)[j];

								string? id = null;
								int slot = -1;
								string? name = null;

								PropertiesStruct savedHistoryData = (PropertiesStruct)history.Value!;
								foreach (FPropertyTag historyProp in savedHistoryData.Properties)
								{
									if (historyProp.Name.Value.Equals("UserID", StringComparison.OrdinalIgnoreCase))
									{
										id = ((FString)historyProp.Property!.Value!).Value;
									}
									else if (historyProp.Name.Value.Equals("ChrSlot", StringComparison.OrdinalIgnoreCase))
									{
										slot = (int)historyProp.Property!.Value!;
									}
									else if (historyProp.Name.Value.Equals("CachedCharacterName", StringComparison.OrdinalIgnoreCase))
									{
										name = ((FString?)historyProp.Property?.Value)?.Value;
									}
								}

								if (id is not null && slot >= 0)
								{
									CharacterID charId = new(id, slot);
									characterNameMap.Add(charId, name ?? $"Unknown-{id}-{slot}");
									characterHistoryIndexMap.Add(charId, j);
								}
							}
							break;
						}
					}
				}
			}

			foreach (RecorderData recorder in playerRecorders)
			{
				CharacterID? charId = null;
				int rocketSpawnId = -1;
				int rocketId = -1;
				int gravestoneId = -1;
				foreach (FPropertyTag prop in recorder.Data)
				{
					if (prop.Name.Value.Equals("PlayerCharacterID", StringComparison.OrdinalIgnoreCase))
					{
						charId = ReadCharacterID(prop.Property!);
					}
					else if (prop.Name.Value.Equals("AssignedDropshipSpawnUID", StringComparison.OrdinalIgnoreCase))
					{
						rocketSpawnId = (int)prop.Property!.Value!;
					}
					else if (prop.Name.Value.Equals("AssignedDropshipUID", StringComparison.OrdinalIgnoreCase))
					{
						rocketId = (int)prop.Property!.Value!;
					}
					else if (prop.Name.Value.Equals("AssignedGravestoneUID", StringComparison.OrdinalIgnoreCase))
					{
						gravestoneId = (int)prop.Property!.Value!;
					}
				}
				if (!charId.HasValue)
				{
					logger.Log(LogLevel.Warning, "Found character data with missing ID. This data will be ignored.");
					continue;
				}

				if (!characterNameMap.TryGetValue(charId.Value, out string? charName))
				{
					charName = null;
				}

				if (!characterHistoryIndexMap.TryGetValue(charId.Value, out int historyIndex))
				{
					historyIndex = -1;
				}

				if (playerStateRecorderMap.TryGetValue(charId.Value, out RecorderData? playerStateRecorder))
				{
					playerStateRecorderMap.Remove(charId.Value);
				}
				else
				{
					playerStateRecorder = null;
				}

				RecorderData? getRecorder(Dictionary<int, RecorderData?> map, int recorderId)
				{
					if (recorderId >= 0 && map.TryGetValue(recorderId, out RecorderData? recorder))
					{
						map.Remove(recorderId);
						return recorder;
					}
					return null;
				}

				RecorderData? rocketSpawnRecorder = getRecorder(rocketSpawnRecorderMap, rocketSpawnId);
				RecorderData? rocketRecorder = getRecorder(rocketRecorderMap, rocketId);
				RecorderData? gravestoneRecorder = getRecorder(gravestoneRecorderMap, gravestoneId);

				List<RecorderData>? ownedMountRecorders;
				if (!playerMountRecorderMap.TryGetValue(charId.Value, out ownedMountRecorders))
				{
					ownedMountRecorders = null;
				}

				characters.Add(new(charId.Value)
				{
					Name = charName,
					RocketSpawnId = rocketSpawnId,
					RocketId = rocketId,
					HistoryIndex = historyIndex,
					PlayerRecorder = recorder,
					PlayerStateRecorder = playerStateRecorder,
					RocketSpawnRecorder = rocketSpawnRecorder,
					RocketRecorder = rocketRecorder,
					GravestoneRecorder = gravestoneRecorder,
					OwnedMountRecorders = ownedMountRecorders
				});
			}

			if (sort)
			{
				characters.Sort();
			}

			if (playerStateRecorderMap.Count > 0)
			{
				result.UnownedPlayerStates = new List<RecorderData>(playerStateRecorderMap.Values.Select(r => r!.Value));
			}
			if (rocketSpawnRecorderMap.Count > 0)
			{
				result.UnownedRocketSpawns = new List<RecorderData>(rocketSpawnRecorderMap.Values.Select(r => r!.Value));
			}
			if (rocketRecorderMap.Count > 0)
			{
				result.UnownedRockets = new List<RecorderData>(rocketRecorderMap.Values.Select(r => r!.Value));
			}
			if (gravestoneRecorderMap.Count > 0)
			{
				result.UnownedGravestones = new List<RecorderData>(gravestoneRecorderMap.Values.Select(r => r!.Value));
			}

			return result;
		}

		/// <summary>
		/// Attempts to read a character id from a struct property
		/// </summary>
		/// <param name="idProperty">The struct property containing a character id</param>
		/// <returns>The character id, or null if no character id could be read</returns>
		public static CharacterID? ReadCharacterID(FProperty idProperty)
		{
			if ((PropertiesStruct)idProperty.Value! is not PropertiesStruct charIdStruct)
			{
				return null;
			}

			string? id = null;
			int slot = -1;
			foreach (FPropertyTag prop in charIdStruct.Properties)
			{
				if (prop.Name.Value.Equals("UserID", StringComparison.OrdinalIgnoreCase) ||
					prop.Name.Value.Equals("PlayerID", StringComparison.OrdinalIgnoreCase))
				{
					id = ((FString?)prop.Property!.Value)?.Value;
				}
				else if (prop.Name.Value.Equals("ChrSlot", StringComparison.OrdinalIgnoreCase))
				{
					slot = (int)prop.Property!.Value!;
				}
			}

			if (id is null) return null;
			return new(id, slot);
		}

		/// <summary>
		/// Writes a new value to a character id struct property
		/// </summary>
		/// <param name="idProperty">A struct property containing a character id</param>
		/// <param name="characterId">The new value to write</param>
		/// <returns>True if the property was updated or false if the property is not a valid character id struct</returns>
		public static bool UpdateCharacterID(FProperty idProperty, CharacterID characterId)
		{
			if ((PropertiesStruct)idProperty.Value! is not PropertiesStruct charIdStruct)
			{
				return false;
			}

			FProperty? playerProperty = null;
			FProperty? slotProperty = null;
			foreach (FPropertyTag prop in charIdStruct.Properties)
			{
				if (prop.Name.Value.Equals("UserID", StringComparison.OrdinalIgnoreCase) ||
					prop.Name.Value.Equals("PlayerID", StringComparison.OrdinalIgnoreCase))
				{
					playerProperty = prop.Property;
				}
				else if (prop.Name.Value.Equals("ChrSlot", StringComparison.OrdinalIgnoreCase))
				{
					slotProperty = prop.Property;
				}
			}

			if (playerProperty is null || slotProperty is null)
			{
				return false;
			}

			playerProperty.Value = characterId.PlayerID is null ? null : new FString(characterId.PlayerID);
			slotProperty.Value = characterId.Slot;
			return true;
		}
	}

	/// <summary>
	/// Data returned from CharacterReader.ReadCharacters
	/// </summary>
	internal struct CharactersData
	{
		public IList<CharacterData> Characters;

		public RecorderData PlayerHistoryRecorder;

		public IReadOnlyList<RecorderData>? UnownedPlayerStates;
		public IReadOnlyList<RecorderData>? UnownedRocketSpawns;
		public IReadOnlyList<RecorderData>? UnownedRockets;
		public IReadOnlyList<RecorderData>? UnownedGravestones;

		public override readonly string ToString()
		{
			return $"{Characters.Count} characters";
		}
	}

	/// <summary>
	/// Data about a specific character
	/// </summary>
	internal struct CharacterData : IEquatable<CharacterData>, IComparable<CharacterData>
	{
		public readonly CharacterID ID;
		public string? Name;
		public int RocketSpawnId;
		public int RocketId;
		public int HistoryIndex;

		public RecorderData PlayerRecorder;
		public RecorderData? PlayerStateRecorder;
		public RecorderData? RocketSpawnRecorder;
		public RecorderData? RocketRecorder;
		public RecorderData? GravestoneRecorder;
		public List<RecorderData>? OwnedMountRecorders;

		public CharacterData(CharacterID id)
		{
			ID = id;
		}

		public override readonly int GetHashCode()
		{
			return ID.GetHashCode();
		}

		public override readonly bool Equals([NotNullWhen(true)] object? obj)
		{
			return obj is CharacterData other && Equals(other);
		}

		public readonly bool Equals(CharacterData other)
		{
			return ID.Equals(other.ID);
		}

		public readonly int CompareTo(CharacterData other)
		{
			return ID.CompareTo(other.ID);
		}

		public override readonly string ToString()
		{
			return $"[{ID}] {Name}";
		}
	}

	/// <summary>
	/// A unique identifier for a character
	/// </summary>
	internal readonly struct CharacterID : IEquatable<CharacterID>, IComparable<CharacterID>
	{
		public static CharacterID Null;

		public readonly string? PlayerID;
		public readonly int Slot;

		static CharacterID()
		{
			Null = new();
		}

		public CharacterID()
		{
			PlayerID = null;
			Slot = -1;
		}

		public CharacterID(string playerID, int slot)
		{
			PlayerID = playerID;
			Slot = slot;
		}

		public static bool TryParse(string value, out CharacterID result)
		{
			string[] parts = value.Split('-');
			if (parts.Length == 1)
			{
				result = new(parts[0], -1);
				return true;
			}
			else if (parts.Length == 2)
			{
				if (int.TryParse(parts[1], out int slot))
				{
					result = new(parts[0], slot);
					return true;
				}
			}

			result = default;
			return false;
		}

		public readonly bool Matches(CharacterID other)
		{
			if (PlayerID != other.PlayerID) return false;
			if (Slot < 0 || other.Slot < 0) return true;
			return Slot == other.Slot;
		}

		public override readonly int GetHashCode()
		{
			return HashCode.Combine(PlayerID, Slot);
		}

		public override readonly bool Equals([NotNullWhen(true)] object? obj)
		{
			return obj is CharacterID other && Equals(other);
		}

		public readonly bool Equals(CharacterID other)
		{
			if (PlayerID is null) return other.PlayerID is null && Slot.Equals(other.Slot);
			return PlayerID.Equals(other.PlayerID) && Slot.Equals(other.Slot);
		}

		public readonly int CompareTo(CharacterID other)
		{
			if (PlayerID is null)
			{
				if (other.PlayerID is not null)
				{
					return -1;
				}
				return Slot.CompareTo(other.Slot);
			}

			int result = PlayerID.CompareTo(other.PlayerID);
			if (result == 0)
			{
				result = Slot.CompareTo(other.Slot);
			}
			return result;
		}

		public override readonly string ToString()
		{
			return $"{PlayerID}-{Slot}";
		}

		public static bool operator ==(CharacterID a, CharacterID b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(CharacterID a, CharacterID b)
		{
			return !a.Equals(b);
		}
	}

	/// <summary>
	/// Data about a recorder from a prospect blob
	/// </summary>
	internal struct RecorderData
	{
		public string Name;
		public int Index;
		public IList<FPropertyTag> Data;

		public void Serialize(ProspectSave prospect)
		{
			FProperty[] recorders = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			PropertiesStruct recorderValue = (PropertiesStruct)recorders[Index].Value!;
			recorderValue.Properties[1] = ProspectSerlializationUtil.SerializeRecorderData(recorderValue.Properties[1], Data);
		}

		public override readonly string ToString()
		{
			return $"[{Index}] {Name} - {Data.Count} properties";
		}
	}

	#endregion

	#region ProgramOptions.cs

	/// <summary>
	/// Represents passed in options for the overall program
	/// </summary>
	internal class ProgramOptions
	{

		/// <summary>
		/// The path to the prospect to read or modify
		/// </summary>
		public string ProspectPath { get; }

		/// <summary>
		/// Instructs the program to print prospect stats
		/// </summary>
		public bool PrintStats { get; }

		/// <summary>
		/// A new name for the prospect
		/// </summary>
		public string? ProspectName { get; }

		/// <summary>
		/// A new privacy setting for the prospect
		/// </summary>
		public ELobbyPrivacy LobbyPrivacy { get; }

		/// <summary>
		/// A new difficulty for the prospect
		/// </summary>
		public EMissionDifficulty Difficulty { get; }

		/// <summary>
		/// A new hardcore setting for the prospect
		/// </summary>
		public bool? Hardcore { get; }

		/// <summary>
		/// A new drop zone for the prospect
		/// </summary>
		public int? DropZone { get; }

		/// <summary>
		/// Mission history commands
		/// </summary>
		public MissionOptions? Mission { get; }

		/// <summary>
		/// Mission generated prebuilt structure commands
		/// </summary>
		public PrebuiltOptions? Prebuilt { get; }

		/// <summary>
		/// Instructs the program to list all players in the prospect
		/// </summary>
		public bool ListPlayers { get; }

		/// <summary>
		/// Instructs the program to cleanup unassociated player related recorders
		/// </summary>
		public bool RunCleanup { get; }

		/// <summary>
		/// A list of players to remove fromt he prospect
		/// </summary>
		public IReadOnlyList<string>? PlayersToRemove { get; }

		public const int MaxOptionStringLength = 24; // Length of "-d, -difficulty [option]"

		public ProgramOptions(
			string prospectPath,
			bool printStats,
			string? prospectName,
			ELobbyPrivacy lobbyPrivacy,
			EMissionDifficulty difficulty,
			bool? hardcore,
			int? dropZone,
			MissionOptions? mission,
			PrebuiltOptions? prebuilt,
			bool listPlayers,
			bool runCleanup,
			IReadOnlyList<string>? playersToRemove)
		{
			ProspectPath = prospectPath;
			PrintStats = printStats;
			ProspectName = prospectName;
			LobbyPrivacy = lobbyPrivacy;
			Difficulty = difficulty;
			Hardcore = hardcore;
			DropZone = dropZone;
			Mission = mission;
			Prebuilt = prebuilt;
			ListPlayers = listPlayers;
			RunCleanup = runCleanup;
			PlayersToRemove = playersToRemove;
		}

		public static void PrintCommandLineOptions(Logger logger, LogLevel logLevel = LogLevel.Information, string indent = "")
		{
			logger.Log(logLevel, $"{indent}-s, -stats                Print statistics about the contents of the prospect save.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-n, -name [value]         Set the prospect name to the supplied value.");
			logger.Log(logLevel, $"{indent}                          Note: This will also change the file name.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-p, -privacy [option]     Set the lobby privacy for the prospect to one of the following.");
			logger.Log(logLevel, $"{indent}                          friends    Steam friends can join.");
			logger.Log(logLevel, $"{indent}                          private    No one can join.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-d, -difficulty [option]  Set the game difficulty for the prospect to one of [easy, medium, hard, extreme].");
			logger.Log(logLevel, $"{indent}                          Warning: Extreme difficulty is only implemented for outposts. Things will break if");
			logger.Log(logLevel, $"{indent}                          you use it elsewhere.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-h, -hardcore [on/off]    Turn on or off the ability to self-respawn if you die in the prospect.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-z, -dropzone [index]     Set the selected drop zone for the prospect.");
			logger.Log(logLevel, $"{indent}                          Warning: Ensure the chosen index is valid for the specific map.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-m, -mission [params]     Commands to manipulate mission history - intended only for open world prospects.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          list           List recorded missions.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          remove [list]  Remove specific missions from the record. Pass in a comma-separated");
			logger.Log(logLevel, $"{indent}                                         list of mission indeces to remove. No spaces.");
			logger.Log(logLevel, $"{indent}                                         Example: remove 0,4,17");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          clear          Remove all missions from the record.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          Warning: Removing a currently active mission may cause problems.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-b, -prebuilt [params]    Commands to manipulate mission generated prebuilt structures.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          list           List prebuilt structures.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          details        List prebuilt structures including details about their contents.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          remove [list]  Remove specific structures. Pass in a comma-separated list of");
			logger.Log(logLevel, $"{indent}                                         structure indeces to remove. No spaces.");
			logger.Log(logLevel, $"{indent}                                         Example: remove 1,3,4");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          clear          Remove all prebuilt structures.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          Warning: Removing a prebuilt structure associated with a currently active mission");
			logger.Log(logLevel, $"{indent}                                   may cause problems.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-l, -list                 Prints information about all player characters stored in the prospect.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-c, -cleanup              Removes any rockets or other player data that is not associated with a valid player.");
			logger.Log(logLevel, $"{indent}                          Run this if you see any warnings when running -list that you want to clean up.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}-r, -remove [players]     Removes listed player characters and their rockets. List a player's Steam ID to");
			logger.Log(logLevel, $"{indent}                          remove all of that player's characters. To remove only a specific character, list");
			logger.Log(logLevel, $"{indent}                          a Steam ID followed by a hyphen, followed by the character slot number. Separate");
			logger.Log(logLevel, $"{indent}                          list entries with commas. Do not include any spaces.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          Example: -r 76561100000000000,76561150505050505-0,76561123232323232");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{indent}                          Warning: Players removed this way will not be able to reclaim their loadout unless");
			logger.Log(logLevel, $"{indent}                          it is insured.");
		}

		public static bool TryParseCommandLine(IReadOnlyList<string> commandLine, Logger logger, [NotNullWhen(true)] out ProgramOptions? options)
		{
			options = null;

			string? prospectPath = null;
			bool printStats = false;
			string? prospectName = null;
			ELobbyPrivacy lobbyPrivacy = ELobbyPrivacy.Unknown;
			EMissionDifficulty difficulty = EMissionDifficulty.None;
			bool? hardcore = null;
			int? dropZone = null;
			MissionOptions? mission = null;
			PrebuiltOptions? prebuilt = null;
			bool listPlayers = false;
			bool runCleanup = false;
			List<string>? playersToRemove = null;

			int positionalArgIndex = 0;

			for (int i = 0; i < commandLine.Count; ++i)
			{
				if (commandLine[i].StartsWith('-'))
				{
					string input = commandLine[i][1..].ToLowerInvariant();

					switch (input)
					{
						case "s":
						case "stats":
							printStats = true;
							break;
						case "n":
						case "name":
							{
								if (prospectName is not null)
								{
									logger.Error("[name] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [value] for parameter [name]");
									return false;
								}

								string subCommand = commandLine[i];
								if (subCommand.StartsWith('-'))
								{
									logger.Error("Missing [value] for parameter [name]");
									return false;
								}

								prospectName = subCommand;
							}
							break;
						case "p":
						case "privacy":
							{
								if (lobbyPrivacy != ELobbyPrivacy.Unknown)
								{
									logger.Error("[privacy] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [option] for parameter [privacy]");
									return false;
								}

								string subCommand = commandLine[i].ToLowerInvariant();
								if (subCommand.StartsWith('-'))
								{
									logger.Error("Missing [option] for parameter [privacy]");
									return false;
								}

								switch (subCommand)
								{
									case "f":
									case "friends":
										lobbyPrivacy = ELobbyPrivacy.FriendsOnly;
										break;
									case "p":
									case "private":
										lobbyPrivacy = ELobbyPrivacy.Private;
										break;
									default:
										logger.Error($"Unrecognized [option] '{subCommand}' for parameter [privacy]");
										return false;
								}
							}
							break;
						case "d":
						case "difficulty":
							{
								if (difficulty != EMissionDifficulty.None)
								{
									logger.Error("[difficulty] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [option] for parameter [difficulty]");
									return false;
								}

								string subCommand = commandLine[i].ToLowerInvariant();
								if (subCommand.StartsWith('-'))
								{
									logger.Error("Missing [option] for parameter [difficulty]");
									return false;
								}

								switch (subCommand)
								{
									case "e":
									case "easy":
										difficulty = EMissionDifficulty.Easy;
										break;
									case "m":
									case "medium":
									case "n":
									case "normal":
										difficulty = EMissionDifficulty.Medium;
										break;
									case "h":
									case "hard":
										difficulty = EMissionDifficulty.Hard;
										break;
									case "ex":
									case "extreme":
										difficulty = EMissionDifficulty.Extreme;
										break;
									default:
										logger.Error($"Unrecognized [option] '{subCommand}' for parameter [difficulty]");
										return false;
								}
							}
							break;
						case "h":
						case "hardcore":
							{
								if (hardcore.HasValue)
								{
									logger.Error("[hardcore] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [on/off] for parameter [hardcore]");
									return false;
								}

								string subCommand = commandLine[i].ToLowerInvariant();
								if (subCommand.StartsWith('-'))
								{
									logger.Error("Missing [on/off] for parameter [hardcore]");
									return false;
								}

								switch (subCommand)
								{
									case "on":
									case "t":
									case "true":
										hardcore = true;
										break;
									case "off":
									case "f":
									case "false":
										hardcore = false;
										break;
									default:
										logger.Error($"Unrecognized [on/off] '{subCommand}' for parameter [hardcore]");
										return false;
								}
							}
							break;
						case "z":
						case "dropzone":
							{
								if (dropZone.HasValue)
								{
									logger.Error("[dropzone] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [index] for parameter [dropzone]");
									return false;
								}

								string subCommand = commandLine[i].ToLowerInvariant();

								int value;
								if (!int.TryParse(subCommand, out value))
								{
									logger.Error($"Expected integer [index] for parameter [dropZone]. Found '{subCommand}'.");
									return false;
								}

								dropZone = value;
							}
							break;
						case "m":
						case "mission":
							{
								if (mission.HasValue)
								{
									logger.Error("[mission] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [params] for parameter [mission]");
									return false;
								}

								MissionCommand command;
								if (!Enum.TryParse(commandLine[i], true, out command))
								{
									logger.Error($"Invalid parameter '{commandLine[i]}' for [mission]");
									return false;
								}

								MissionOptions missionOptions = new()
								{
									Command = command
								};

								if (command == MissionCommand.Remove)
								{
									++i;
									if (i >= commandLine.Count)
									{
										logger.Error("Missing [params] for parameter [mission]");
										return false;
									}

									string[] missionParams = commandLine[i].Split(',').Select(item => item.Trim()).ToArray();
									missionOptions.Parameters = new int[missionParams.Length];
									for (int p = 0; p < missionParams.Length; ++p)
									{
										if (!int.TryParse(missionParams[p], out int value))
										{
											logger.Error($"Invalid value '{missionParams[p]}' for parameter [mission remove]. Values must be integers.");
											return false;
										}
										missionOptions.Parameters[p] = value;
									}
								}

								mission = missionOptions;
							}
							break;
						case "b":
						case "prebuilt":
							{
								if (prebuilt.HasValue)
								{
									logger.Error("[prebuilt] parameter found more than once");
									return false;
								}

								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [params] for parameter [prebuilt]");
									return false;
								}

								PrebuiltCommand command;
								if (!Enum.TryParse(commandLine[i], true, out command))
								{
									logger.Error($"Invalid parameter '{commandLine[i]}' for [prebuilt]");
									return false;
								}

								PrebuiltOptions prebuiltOptions = new()
								{
									Command = command
								};

								if (command == PrebuiltCommand.Remove)
								{
									++i;
									if (i >= commandLine.Count)
									{
										logger.Error("Missing [params] for parameter [prebuilt]");
										return false;
									}

									string[] prebuiltParams = commandLine[i].Split(',').Select(item => item.Trim()).ToArray();
									prebuiltOptions.Parameters = new int[prebuiltParams.Length];
									for (int p = 0; p < prebuiltParams.Length; ++p)
									{
										if (!int.TryParse(prebuiltParams[p], out int value))
										{
											logger.Error($"Invalid value '{prebuiltParams[p]}' for parameter [prebuilt remove]. Values must be integers.");
											return false;
										}
										prebuiltOptions.Parameters[p] = value;
									}
								}

								prebuilt = prebuiltOptions;
							}
							break;
						case "l":
						case "list":
							{
								listPlayers = true;
							}
							break;
						case "c":
						case "cleanup":
							{
								runCleanup = true;
							}
							break;
						case "r":
						case "remove":
							{
								++i;
								if (i >= commandLine.Count)
								{
									logger.Error("Missing [players] for parameter [remove]");
									return false;
								}

								string subCommand = commandLine[i].ToLowerInvariant();

								// If [remove] is passed more than once, concatenate the values
								if (playersToRemove is null)
								{
									playersToRemove = new();
								}
								playersToRemove.AddRange(subCommand.Split(','));
							}
							break;
						default:
							logger.Error($"Unrecognized argument '{commandLine[i]}'");
							return false;
					}
				}
				else
				{
					// Positional arg
					switch (positionalArgIndex)
					{
						case 0:
							prospectPath = Path.GetFullPath(commandLine[i]);
							break;
						default:
							logger.Error("Too many positional arguments.");
							return false;
					}
					++positionalArgIndex;
				}
			}

			if (prospectPath is null)
			{
				logger.Error("Missing prospect path argument");
				return false;
			}

			options = new ProgramOptions(prospectPath, printStats, prospectName, lobbyPrivacy, difficulty, hardcore, dropZone, mission, prebuilt, listPlayers, runCleanup, playersToRemove);
			return true;
		}

		public bool Any()
		{
			return PrintStats
				|| ProspectName is not null
				|| LobbyPrivacy != ELobbyPrivacy.Unknown
				|| Difficulty != EMissionDifficulty.None
				|| Hardcore.HasValue
				|| DropZone.HasValue
				|| Mission.HasValue
				|| Prebuilt.HasValue
				|| ListPlayers
				|| RunCleanup
				|| PlayersToRemove is not null;
		}
	}

	internal struct MissionOptions
	{
		public MissionCommand Command;
		public int[] Parameters;
	}

	internal struct PrebuiltOptions
	{
		public PrebuiltCommand Command;
		public int[] Parameters;
	}

	internal enum MissionCommand
	{
		List,
		Remove,
		Clear
	}

	internal enum PrebuiltCommand
	{
		List,
		Details,
		Remove,
		Clear
	}

	internal enum ELobbyPrivacy
	{
		Unknown,
		FriendsOnly,
		Private
	}

	internal enum EMissionDifficulty
	{
		None,
		Easy,
		Medium,
		Hard,
		Extreme
	}

	#endregion

	#region ProspectEditor.cs

	/// <summary>
	/// Processes/updates a prospect save
	/// </summary>
	internal class ProspectEditor
	{
		private readonly Logger mLogger;

		public ProspectEditor(Logger logger)
		{
			mLogger = logger;
		}

		/// <summary>
		/// Process/update a prospect
		/// </summary>
		/// <param name="prospect">The prospect to update</param>
		/// <param name="options">Decsription of updates to perform</param>
		/// <returns>True if the prospect has been modified as a result of this run, else false</returns>
		public bool Run(ProspectSave prospect, ProgramOptions options)
		{
			ArrayProperty? stateRecorderBlobs = prospect.ProspectData[0].Property as ArrayProperty;
			if (stateRecorderBlobs?.Value == null)
			{
				mLogger.Error("Error reading prospect. Failed to locate state recorder array at index 0.");
				return false;
			}

			mLogger.Log(LogLevel.Important, "Processing...");

			bool changed = false;

			if (options.PrintStats)
			{
				if (!PrintStats(prospect))
				{
					return false;
				}
			}

			if (options.ProspectName is not null)
			{
				if (!UpdateProspectName(prospect, options.ProspectName))
				{
					return false;
				}
				changed = true;
			}

			if (options.LobbyPrivacy != ELobbyPrivacy.Unknown)
			{
				if (!UpdateLobbyPrivacy(prospect, options.LobbyPrivacy))
				{
					return false;
				}
				changed = true;
			}

			if (options.Difficulty != EMissionDifficulty.None)
			{
				if (!UpdateDifficulty(prospect, options.Difficulty))
				{
					return false;
				}
				changed = true;
			}

			if (options.Hardcore.HasValue)
			{
				if (!UpdateHardcore(prospect, options.Hardcore.Value))
				{
					return false;
				}
				changed = true;
			}

			if (options.DropZone.HasValue)
			{
				if (!UpdateDropZone(prospect, options.DropZone.Value))
				{
					return false;
				}
				changed = true;
			}

			if (options.Mission.HasValue)
			{
				if (!ProcessMissionHistory(prospect, options.Mission.Value))
				{
					return false;
				}
				changed |= options.Mission.Value.Command != MissionCommand.List;
			}

			if (options.Prebuilt.HasValue)
			{
				if (!ProcessPrebuilts(prospect, options.Prebuilt.Value))
				{
					return false;
				}
				changed |= options.Prebuilt.Value.Command != PrebuiltCommand.List
					&& options.Prebuilt.Value.Command != PrebuiltCommand.Details;
			}

			if (options.ListPlayers)
			{
				if (!ListPlayers(prospect))
				{
					return false;
				}
			}

			if (options.RunCleanup)
			{
				if (!CleanupUnassociatedRecorders(prospect))
				{
					return false;
				}
				changed = true;
			}

			if (options.PlayersToRemove is not null)
			{
				if (!RemovePlayers(prospect, options.PlayersToRemove))
				{
					return false;
				}
				changed = true;
			}

			return changed;
		}

		private bool PrintStats(ProspectSave prospect)
		{
			mLogger.Log(LogLevel.Information, "Reading prospect stats...");

			Dictionary<string, int> recorderCounts = new(StringComparer.OrdinalIgnoreCase);
			int gameStateSeed = 0;
			float timeOfDay = 0.0f;
			float prospectGameTime = 0.0f;
			int secondsPerDay = 4060;
			int dynamicQuestSeed = 0;
			int nextMeteorTime = 0;
			Dictionary<string, int> deployableCounts = new(StringComparer.OrdinalIgnoreCase);
			Dictionary<int, int> buildingGridCounts = new();

			FProperty[] recorderProperties = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			for (int i = 0; i < recorderProperties.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorderProperties[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;

				recorderCounts[recorderName] = recorderCounts.TryGetValue(recorderName, out int existingCount) ? existingCount + 1 : 1;

				if (recorderName.Equals("/Script/Icarus.GameModeStateRecorderComponent"))
				{
					IList<FPropertyTag> gameModeRecorderProperties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
					foreach (FPropertyTag property in gameModeRecorderProperties)
					{
						if (property.Name.Equals("GameModeRecord"))
						{
							PropertiesStruct recordProperties = (PropertiesStruct)property.Property!.Value!;
							foreach (FPropertyTag recordProperty in recordProperties.Properties)
							{
								if (recordProperty.Name.Equals("GameStateSeed")) gameStateSeed = ((IntProperty)recordProperty.Property!).Value;
								else if (recordProperty.Name.Equals("TimeOfDay")) timeOfDay = ((FloatProperty)recordProperty.Property!).Value;
								else if (recordProperty.Name.Equals("ProspectGameTime")) prospectGameTime = ((FloatProperty)recordProperty.Property!).Value;
								else if (recordProperty.Name.Equals("SecondsPerGameDay")) secondsPerDay = ((IntProperty)recordProperty.Property!).Value;
							}
						}
						else if (property.Name.Equals("DynamicQuestSeed")) dynamicQuestSeed = ((IntProperty)property.Property!).Value;
						else if (property.Name.Equals("NextMeteorShowerTime")) nextMeteorTime = ((IntProperty)property.Property!).Value;
					}
				}
				else if (recorderName.Equals("/Script/Icarus.DeployableRecorderComponent"))
				{
					IList<FPropertyTag> deployableRecorderProperties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
					FPropertyTag? itemDataProperty = deployableRecorderProperties.FirstOrDefault(p => p.Name.Equals("StaticItemDataRowName"));
					if (itemDataProperty is not null)
					{
						string itemName = ((NameProperty)itemDataProperty.Property!).Value!.Value;
						deployableCounts[itemName] = deployableCounts.TryGetValue(itemName, out int deployableCount) ? deployableCount + 1 : 1;
					}
				}
				else if (recorderName.Equals("/Script/Icarus.BuildingGridRecorderComponent"))
				{
					IList<FPropertyTag> gridRecorderProperties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
					int actorGuid = ((IntProperty?)gridRecorderProperties.FirstOrDefault(p => p.Name.Equals("IcarusActorGUID"))?.Property)?.Value ?? 0;
					FPropertyTag? gridRecordProperty = gridRecorderProperties.FirstOrDefault(p => p.Name.Equals("BuildingGridRecord"));
					ArrayProperty? buildTypesProperty = ((PropertiesStruct)gridRecordProperty?.Property!.Value!).Properties.FirstOrDefault(p => p.Name.Equals("BuildingTypes"))?.Property as ArrayProperty;
					if (buildTypesProperty is not null)
					{
						int buildingPieceCount = 0;
						foreach (StructProperty buildTypeEntry in buildTypesProperty.Value!)
						{
							ArrayProperty? instancesProperty = ((PropertiesStruct)buildTypeEntry.Value!).Properties.FirstOrDefault(p => p.Name.Equals("BuildingInstances"))?.Property as ArrayProperty;
							if (instancesProperty is not null) buildingPieceCount += instancesProperty.Value!.Length;
						}
						buildingGridCounts[actorGuid] = buildingGridCounts.TryGetValue(actorGuid, out int existingGridCount) ? existingGridCount + buildingPieceCount : buildingPieceCount;
					}
				}
			}

			int timeOfDayConverted = (int)(timeOfDay / secondsPerDay * 24 * 3600);

			Table infoTable = CreateTable("[deepskyblue1]Property[/]", "[deepskyblue1]Value[/]");
			infoTable.Title = new TableTitle("[yellow]Prospect information[/]");
			infoTable.AddRow("Name", EscapeMarkup(prospect.ProspectInfo.ProspectID));
			infoTable.AddRow("Type", EscapeMarkup(prospect.ProspectInfo.ProspectDTKey));
			infoTable.AddRow("Difficulty", EscapeMarkup(prospect.ProspectInfo.Difficulty));
			infoTable.AddRow("Hardcore", EscapeMarkup(prospect.ProspectInfo.NoRespawns.ToString()));
			infoTable.AddRow("Drop zone", prospect.ProspectInfo.SelectedDropPoint.ToString());
			infoTable.AddRow("Game state seed", gameStateSeed.ToString());
			infoTable.AddRow("Dynamic quest seed", dynamicQuestSeed.ToString());
			infoTable.AddRow("Prospect time", EscapeMarkup(FormatTimestamp((int)prospectGameTime)));
			infoTable.AddRow("Next exotic respawn time", EscapeMarkup(FormatTimestamp((int)nextMeteorTime)));
			infoTable.AddRow("Time of day", EscapeMarkup(FormatTimestamp(timeOfDayConverted, false)));
			infoTable.AddRow("Players", prospect.ProspectInfo.AssociatedMembers.Count.ToString());
			mLogger.Render(infoTable);

			Table recorderTable = CreateTable("[deepskyblue1]Recorder[/]", "[deepskyblue1]Count[/]");
			recorderTable.Title = new TableTitle("[yellow]Recorder counts[/]");
			foreach (var pair in recorderCounts.OrderBy(kvp => kvp.Key))
			{
				recorderTable.AddRow(EscapeMarkup(FormatRecorderName(pair.Key)), pair.Value.ToString());
			}
			mLogger.Render(recorderTable);

			if (deployableCounts.Count > 0)
			{
				Table deployableTable = CreateTable("[deepskyblue1]Deployable[/]", "[deepskyblue1]Count[/]");
				deployableTable.Title = new TableTitle("[yellow]Deployable counts[/]");
				foreach (var pair in deployableCounts.OrderBy(kvp => kvp.Key)) deployableTable.AddRow(EscapeMarkup(pair.Key), pair.Value.ToString());
				mLogger.Render(deployableTable);
			}

			if (buildingGridCounts.Count > 0)
			{
				Table buildingGridTable = CreateTable("[deepskyblue1]Grid actor[/]", "[deepskyblue1]Pieces[/]");
				buildingGridTable.Title = new TableTitle("[yellow]Building grid pieces[/]");
				foreach (var pair in buildingGridCounts.OrderBy(kvp => kvp.Key)) buildingGridTable.AddRow(pair.Key.ToString(), pair.Value.ToString());
				buildingGridTable.AddRow("[bold]Total[/]", $"[bold]{buildingGridCounts.Sum(kvp => kvp.Value)}[/]");
				mLogger.Render(buildingGridTable);
			}

			return true;
		}

		private bool UpdateProspectName(ProspectSave prospect, string name)
		{
			string oldName = prospect.ProspectInfo.ProspectID;

			FProspectInfo prospectInfo = prospect.ProspectInfo;
			prospectInfo.ProspectID = name;
			prospect.ProspectInfo = prospectInfo;

			StrProperty? prospectIdProperty = GetProspectInfoProperty<StrProperty>(prospect, nameof(FProspectInfo.ProspectID));
			if (prospectIdProperty is null)
			{
				return false;
			}

			prospectIdProperty.Value = new FString(name);

			mLogger.Log(LogLevel.Information, $"Prospect name changed from '{oldName}' to '{name}'");

			return true;
		}

		private bool UpdateLobbyPrivacy(ProspectSave prospect, ELobbyPrivacy lobbyPrivacy)
		{
			EnumProperty? lobbyPrivacyProperty = prospect.ProspectData.FirstOrDefault(p => p.Name.Equals("LobbyPrivacy"))?.Property as EnumProperty;
			if (lobbyPrivacyProperty is null)
			{
				mLogger.Error("Error locating lobby privacy property");
				return false;
			}

			string oldLobbyPrivacy = GetEnumValue(lobbyPrivacyProperty.Value?.Value, ELobbyPrivacy.Unknown.ToString());

			lobbyPrivacyProperty.Value = new FString($"{nameof(ELobbyPrivacy)}::{lobbyPrivacy}");

			mLogger.Log(LogLevel.Information, $"Lobby privacy changed from '{oldLobbyPrivacy}' to '{lobbyPrivacy}'.");

			return true;
		}

		private bool UpdateDifficulty(ProspectSave prospect, EMissionDifficulty difficulty)
		{
			FProspectInfo prospectInfo = prospect.ProspectInfo;
			prospectInfo.Difficulty = difficulty.ToString();
			prospect.ProspectInfo = prospectInfo;

			EnumProperty? difficultyProperty = GetProspectInfoProperty<EnumProperty>(prospect, nameof(FProspectInfo.Difficulty));
			if (difficultyProperty is null)
			{
				return false;
			}

			string oldDifficulty = GetEnumValue(difficultyProperty.Value?.Value, EMissionDifficulty.None.ToString());

			difficultyProperty.Value = new FString($"{nameof(EMissionDifficulty)}::{difficulty}");

			mLogger.Log(LogLevel.Information, $"Difficulty changed from '{oldDifficulty}' to '{difficulty}'.");

			return true;
		}

		private bool UpdateHardcore(ProspectSave prospect, bool enable)
		{
			FProspectInfo prospectInfo = prospect.ProspectInfo;
			prospectInfo.NoRespawns = enable;
			prospect.ProspectInfo = prospectInfo;

			BoolProperty? noRespawnsProperty = GetProspectInfoProperty<BoolProperty>(prospect, nameof(FProspectInfo.NoRespawns));
			if (noRespawnsProperty is null)
			{
				return false;
			}

			string oldEnable = noRespawnsProperty.Value ? "on" : "off";

			noRespawnsProperty.Value = enable;

			mLogger.Log(LogLevel.Information, $"Hardcore changed from '{oldEnable}' to '{(enable ? "on" : "off")}'.");

			return true;
		}

		private bool UpdateDropZone(ProspectSave prospect, int dropZone)
		{
			FProspectInfo prospectInfo = prospect.ProspectInfo;
			prospectInfo.SelectedDropPoint = dropZone;
			prospect.ProspectInfo = prospectInfo;

			IntProperty? selectedDropPointProperty = GetProspectInfoProperty<IntProperty>(prospect, nameof(FProspectInfo.SelectedDropPoint));
			if (selectedDropPointProperty is null)
			{
				return false;
			}

			int oldDropZone = selectedDropPointProperty.Value;

			selectedDropPointProperty.Value = dropZone;

			mLogger.Log(LogLevel.Information, $"Drop zone changed from '{oldDropZone}' to '{dropZone}'.");

			return true;
		}

		private bool ProcessMissionHistory(ProspectSave prospect, MissionOptions options)
		{
			switch (options.Command)
			{
				case MissionCommand.List:
					return ListMissionHistory(prospect);
				case MissionCommand.Remove:
					return RemoveFromMissionHistory(prospect, options.Parameters);
				case MissionCommand.Clear:
					return ClearMissionHistory(prospect);
				default:
					mLogger.Error("Invalid mission history command");
					return false;
			}
		}

		private bool ListMissionHistory(ProspectSave prospect)
		{
			if (!TryGetMissionHistoryProperty(prospect, out ArrayProperty? missionHistory, out PropertiesStruct? recorder, out IList<FPropertyTag>? recorderProperties))
			{
				mLogger.Error("Error: Unable to locate mission history");
				return false;
			}

			mLogger.Log(LogLevel.Information, "Listing mission history records");
			Table table = CreateTable("[deepskyblue1]Index[/]", "[deepskyblue1]Mission Name[/]", "[deepskyblue1]Status[/]", "[deepskyblue1]End Time[/]");
			table.Title = new TableTitle("[yellow]Mission history[/]");

			for (int i = 0; i < missionHistory.Value!.Length; ++i)
			{
				StructProperty historyProperty = (StructProperty)missionHistory.Value!.GetValue(i)!;
				string? name = null;
				int? status = null;
				int? endTime = null;
				foreach (FPropertyTag entryProperty in ((PropertiesStruct)(historyProperty.Value!)).Properties)
				{
					switch (entryProperty.Name.Value)
					{
						case "Mission": name = ((StrProperty)entryProperty.Property!).Value!.Value; break;
						case "Status": status = ((IntProperty)entryProperty.Property!).Value; break;
						case "MissionEndTime": endTime = ((IntProperty)entryProperty.Property!).Value; break;
					}
				}
				table.AddRow(i.ToString(), EscapeMarkup(name ?? "NAME_MISSING"), EscapeMarkup(!status.HasValue ? "STATUS_MISSING" : ((EMissionState)status.Value).ToString()), EscapeMarkup(endTime.HasValue ? FormatTimestamp(endTime.Value) ?? string.Empty : string.Empty));
			}

			mLogger.Render(table);
			return true;
		}

		private bool RemoveFromMissionHistory(ProspectSave prospect, int[] missionsToRemove)
		{
			if (!TryGetMissionHistoryProperty(prospect, out ArrayProperty? missionHistory, out PropertiesStruct? recorder, out IList<FPropertyTag>? recorderProperties))
			{
				mLogger.Error("Error: Unable to locate mission history");
				return false;
			}

			mLogger.Log(LogLevel.Information, $"Removing mission history records at indeces: {string.Join(',', missionsToRemove)}");

			HashSet<int> toRemove = new(missionsToRemove);

			FProperty[] newHistory = new FProperty[missionHistory.Value!.Length - missionsToRemove.Length];
			for (int inIndex = 0, outIndex = 0; inIndex < missionHistory.Value!.Length; ++inIndex)
			{
				if (toRemove.Contains(inIndex)) continue;

				newHistory[outIndex] = (FProperty)missionHistory.Value!.GetValue(inIndex)!;
				++outIndex;
			}
			missionHistory.Value = newHistory;

			recorder.Properties[1] = ProspectSerlializationUtil.SerializeRecorderData(recorder.Properties[1], recorderProperties);

			return true;
		}

		private bool ClearMissionHistory(ProspectSave prospect)
		{
			if (!TryGetMissionHistoryProperty(prospect, out ArrayProperty? missionHistory, out PropertiesStruct? recorder, out IList<FPropertyTag>? recorderProperties))
			{
				mLogger.Error("Error: Unable to locate mission history");
				return false;
			}

			mLogger.Log(LogLevel.Information, "Removing all history records");

			missionHistory.Value = new FProperty[0];
			recorder.Properties[1] = ProspectSerlializationUtil.SerializeRecorderData(recorder.Properties[1], recorderProperties);

			return true;
		}

		private static bool TryGetMissionHistoryProperty(
			ProspectSave prospect,
			[NotNullWhen(true)] out ArrayProperty? array,
			[NotNullWhen(true)] out PropertiesStruct? recorder,
			[NotNullWhen(true)] out IList<FPropertyTag>? recorderProperties)
		{
			array = null;
			recorder = null;
			recorderProperties = null;

			FProperty[] recorders = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			for (int i = 0; i < recorders.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorders[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;

				if (!recorderName.Equals("/Script/Icarus.GameModeStateRecorderComponent")) continue;

				IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);

				FPropertyTag? missionHistoryProperty = properties.FirstOrDefault(p => p.Name.Equals("MissionHistory"));
				if (missionHistoryProperty is null)
				{
					return false;
				}

				array = (ArrayProperty)missionHistoryProperty.Property!;
				recorder = recorderValue;
				recorderProperties = properties;
				return true;
			}

			return false;
		}

		private bool ProcessPrebuilts(ProspectSave prospect, PrebuiltOptions options)
		{
			switch (options.Command)
			{
				case PrebuiltCommand.List:
					return ListPrebuilts(prospect);
				case PrebuiltCommand.Details:
					return ListPrebuiltsWithDetails(prospect);
				case PrebuiltCommand.Remove:
					return RemovePrebuilts(prospect, options.Parameters);
				case PrebuiltCommand.Clear:
					return ClearPrebuilts(prospect);
				default:
					mLogger.Error("Invalid prebuilt command");
					return false;
			}
		}

		private bool ListPrebuilts(ProspectSave prospect)
		{
			mLogger.Log(LogLevel.Information, "Listing prebuilt structures");
			Table table = CreateTable("[deepskyblue1]Index[/]", "[deepskyblue1]Structure Name[/]", "[deepskyblue1]Actors[/]", "[deepskyblue1]Location[/]");
			table.Title = new TableTitle("[yellow]Prebuilt structures[/]");

			FProperty[] recorderProperties = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			for (int i = 0, prebuiltIndex = 0; i < recorderProperties.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorderProperties[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;
				if (!recorderName.Equals("/Script/Icarus.PrebuiltStructureRecorderComponent")) continue;
				IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
				string structureName = "NAME_MISSING";
				int actorCount = 0;
				FVector? structureLocation = null;
				foreach (FPropertyTag property in properties)
				{
					switch (property.Name.Value)
					{
						case "PrebuiltStructureName": structureName = ((NameProperty)property.Property!).Value!.Value; break;
						case "RelevantActorRecords": actorCount = ((ArrayProperty)property.Property!).Value!.Length; break;
						case "ActorTransform": structureLocation = ((VectorStruct)((PropertiesStruct)property.Property!.Value!).Properties.FirstOrDefault(p => p.Name.Equals("Translation"))?.Property!.Value!).Value; break;
					}
				}
				table.AddRow(prebuiltIndex.ToString(), EscapeMarkup(structureName), actorCount.ToString(), EscapeMarkup(FormatVector(structureLocation) ?? string.Empty));
				++prebuiltIndex;
			}

			mLogger.Render(table);
			return true;
		}

		private bool ListPrebuiltsWithDetails(ProspectSave prospect)
		{
			mLogger.Log(LogLevel.Information, "Listing prebuilt structures with details");
			Table table = CreateTable("[deepskyblue1]Index[/]", "[deepskyblue1]Structure Name[/]", "[deepskyblue1]Actors[/]", "[deepskyblue1]Location[/]", "[deepskyblue1]Recorder breakdown[/]");
			table.Title = new TableTitle("[yellow]Prebuilt structure details[/]");

			List<PropertiesStruct> prebuiltStructureRecorders = new();
			Dictionary<int, List<int>> actorToIndexMap = new();
			Dictionary<int, string> recorderIndexToNameMap = new();

			void checkActor(int index, IList<FPropertyTag> properties)
			{
				FPropertyTag? actorGuidProperty = properties.FirstOrDefault(p => p.Name.Equals("IcarusActorGUID"));
				if (actorGuidProperty is not null && actorGuidProperty.Property is IntProperty asIntProperty && asIntProperty.Value != 0)
				{
					if (!actorToIndexMap.TryGetValue(asIntProperty.Value, out List<int>? value))
					{
						value = new();
						actorToIndexMap.Add(asIntProperty.Value, value);
					}
					value.Add(index);
				}
			}

			FProperty[] recorderProperties = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			for (int i = 0; i < recorderProperties.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorderProperties[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;
				recorderIndexToNameMap.Add(i, recorderName);
				if (recorderName.Equals("/Script/Icarus.PrebuiltStructureRecorderComponent"))
				{
					prebuiltStructureRecorders.Add(recorderValue);
				}
				else if (recorderName.Equals("/Script/Icarus.BuildingGridRecorderComponent"))
				{
					IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
					checkActor(i, properties);
					foreach (int uid in GetBuildingGridPieceUIDs(properties))
					{
						if (!actorToIndexMap.TryGetValue(uid, out List<int>? value))
						{
							value = new();
							actorToIndexMap.Add(uid, value);
						}
						value.Add(i);
					}
				}
				else
				{
					checkActor(i, ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]));
				}
			}

			for (int prebuiltIndex = 0; prebuiltIndex < prebuiltStructureRecorders.Count; ++prebuiltIndex)
			{
				IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(prebuiltStructureRecorders[prebuiltIndex].Properties[1]);
				string structureName = "NAME_MISSING";
				FProperty[]? relevantActors = null;
				int actorCount = 0;
				FVector? structureLocation = null;
				foreach (FPropertyTag property in properties)
				{
					switch (property.Name.Value)
					{
						case "PrebuiltStructureName": structureName = ((NameProperty)property.Property!).Value!.Value; break;
						case "RelevantActorRecords": relevantActors = (FProperty[])((ArrayProperty)property.Property!).Value!; actorCount = relevantActors.Length; break;
						case "ActorTransform": structureLocation = ((VectorStruct)((PropertiesStruct)property.Property!.Value!).Properties.FirstOrDefault(p => p.Name.Equals("Translation"))?.Property!.Value!).Value; break;
					}
				}

				Dictionary<string, int> recorderCountsByType = new();
				if (relevantActors is not null)
				{
					foreach (FProperty relevantActorProperty in relevantActors)
					{
						IntProperty? uidProperty = (IntProperty?)((PropertiesStruct)relevantActorProperty.Value!).Properties.FirstOrDefault(p => p.Name.Equals("RelevantActorIcarusUID"))?.Property;
						if (uidProperty is null) continue;
						if (actorToIndexMap.TryGetValue(uidProperty.Value, out List<int>? recorderIndices))
						{
							foreach (int recorderIndex in recorderIndices)
							{
								string recorderName = recorderIndexToNameMap[recorderIndex];
								recorderCountsByType[recorderName] = recorderCountsByType.TryGetValue(recorderName, out int count) ? count + 1 : 1;
							}
						}
						else
						{
							const string unknownRecorderName = "UNKNOWN";
							recorderCountsByType[unknownRecorderName] = recorderCountsByType.TryGetValue(unknownRecorderName, out int count) ? count + 1 : 1;
						}
					}
				}

				string breakdown = string.Join(Environment.NewLine, recorderCountsByType.OrderBy(kvp => kvp.Key).Select(pair => $"{FormatRecorderName(pair.Key)}: {pair.Value}"));
				table.AddRow(prebuiltIndex.ToString(), EscapeMarkup(structureName), actorCount.ToString(), EscapeMarkup(FormatVector(structureLocation) ?? string.Empty), EscapeMarkup(breakdown));
			}

			mLogger.Render(table);
			return true;
		}

		private bool RemovePrebuilts(ProspectSave prospect, int[] prebuiltsToRemove)
		{
			mLogger.Log(LogLevel.Information, $"Removing prebuilt structures at indeces: {string.Join(',', prebuiltsToRemove)}");
			return InternalRemovePrebuilts(prospect, false, prebuiltsToRemove);
		}

		private bool ClearPrebuilts(ProspectSave prospect)
		{
			mLogger.Log(LogLevel.Information, "Removing all prebuilt structures");
			return InternalRemovePrebuilts(prospect, true, new int[0]);
		}

		private bool InternalRemovePrebuilts(ProspectSave prospect, bool clear, int[] prebuiltsToRemove)
		{
			List<PropertiesStruct> prebuiltStructureRecorders = new();
			Dictionary<int, List<int>> actorToIndexMap = new();

			HashSet<int> recordersToRemove = new();
			HashSet<int> prebuiltRemoveSet = new(prebuiltsToRemove);

			void checkActor(int index, IList<FPropertyTag> properties)
			{
				FPropertyTag? actorGuidProperty = properties.FirstOrDefault(p => p.Name.Equals("IcarusActorGUID"));
				if (actorGuidProperty is not null && actorGuidProperty.Property is IntProperty asIntProperty && asIntProperty.Value != 0)
				{
					List<int>? value;
					if (!actorToIndexMap.TryGetValue(asIntProperty.Value, out value))
					{
						value = new();
						actorToIndexMap.Add(asIntProperty.Value, value);
					}
					value.Add(index);
				}
			}

			FProperty[] recorderProperties = (FProperty[])prospect.ProspectData[0].Property!.Value!;
			for (int i = 0, prebuiltIndex = 0; i < recorderProperties.Length; ++i)
			{
				StructProperty recorderProperty = (StructProperty)recorderProperties[i];
				PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
				string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;

				if (recorderName.Equals("/Script/Icarus.PrebuiltStructureRecorderComponent"))
				{
					if (clear || prebuiltRemoveSet.Contains(prebuiltIndex))
					{
						prebuiltStructureRecorders.Add(recorderValue);
						recordersToRemove.Add(i);
					}
					++prebuiltIndex;
				}
				else if (recorderName.Equals("/Script/Icarus.BuildingGridRecorderComponent"))
				{
					IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);

					checkActor(i, properties);

					IEnumerable<int> pieceUIDs = GetBuildingGridPieceUIDs(properties);
					foreach (int uid in pieceUIDs)
					{
						List<int>? value;
						if (!actorToIndexMap.TryGetValue(uid, out value))
						{
							value = new();
							actorToIndexMap.Add(uid, value);
						}
						value.Add(i);
					}
				}
				else
				{
					IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(recorderValue.Properties[1]);
					checkActor(i, properties);
				}
			}

			HashSet<int> actorsToRemove = new();

			foreach (PropertiesStruct prebuiltStructureRecorder in prebuiltStructureRecorders)
			{
				IList<FPropertyTag> properties = ProspectSerlializationUtil.DeserializeRecorderData(prebuiltStructureRecorder.Properties[1]);

				foreach (FPropertyTag property in properties)
				{
					if (!property.Name.Equals("RelevantActorRecords")) continue;

					FProperty[] relevantActors = (FProperty[])((ArrayProperty)property.Property!).Value!;
					foreach(FProperty relevantActorProperty in relevantActors)
					{
						IntProperty? uidProperty = (IntProperty?)((PropertiesStruct)relevantActorProperty.Value!).Properties.FirstOrDefault(p => p.Name.Equals("RelevantActorIcarusUID"))?.Property;
						if (uidProperty is not null)
						{
							actorsToRemove.Add(uidProperty.Value);
						}
					}

					break;
				}
			}

			foreach (int actor in actorsToRemove)
			{
				List<int>? recorders;
				if (actorToIndexMap.TryGetValue(actor, out recorders))
				{
					if (recorders.Count > 1)
					{
						mLogger.Log(LogLevel.Debug, $"Found actor {actor} in {recorders.Count} recorders. Skipping");
					}
					else
					{
						foreach (int recorder in recorders)
						{
							recordersToRemove.Add(recorder);
						}
					}
				}
				else
				{
					mLogger.Log(LogLevel.Information, $"Could not locate actor to remove: {actor}");
				}
			}

			if (mLogger.LogLevel <= LogLevel.Debug)
			{
				mLogger.Log(LogLevel.Debug, "Removing recorders:");
				foreach (int index in recordersToRemove.OrderBy(i => i))
				{
					StructProperty recorderProperty = (StructProperty)recorderProperties[index];
					PropertiesStruct recorderValue = (PropertiesStruct)recorderProperty.Value!;
					string recorderName = ((FString)recorderValue.Properties[0].Property!.Value!).Value;
					mLogger.Log(LogLevel.Debug, $"{index.ToString().PadLeft(7)} {recorderName}");
				}
			}

			List<FProperty> outRecorders = new();
			for (int i = 0; i < recorderProperties.Length; ++i)
			{
				if (recordersToRemove.Contains(i)) continue;

				outRecorders.Add(recorderProperties[i]);
			}

			prospect.ProspectData[0].Property!.Value = outRecorders.ToArray();

			return true;
		}

		private static ISet<int> GetBuildingGridPieceUIDs(IEnumerable<FPropertyTag> recorderProperties)
		{
			HashSet<int> uids = new();

			FPropertyTag? buildingGridRecordProperty = recorderProperties.FirstOrDefault(p => p.Name.Equals("BuildingGridRecord"));
			if (buildingGridRecordProperty is not null)
			{
				FPropertyTag? buildingTypesProperty = ((PropertiesStruct?)((StructProperty?)buildingGridRecordProperty.Property)?.Value)?.Properties.FirstOrDefault(p => p.Name.Equals("BuildingTypes"));
				if (buildingTypesProperty is not null)
				{
					FProperty[] buildingTypes = (FProperty[])((ArrayProperty)buildingTypesProperty.Property!).Value!;
					foreach (StructProperty buildingTypeProperty in buildingTypes)
					{
						FPropertyTag? buildingInstancesProperty = ((PropertiesStruct)buildingTypeProperty.Value!).Properties.FirstOrDefault(p => p.Name.Equals("BuildingInstances"));
						if (buildingInstancesProperty is not null)
						{
							FProperty[] buildingInstances = (FProperty[])((ArrayProperty)buildingInstancesProperty.Property!).Value!;
							foreach (StructProperty buildingInstanceProperty in buildingInstances)
							{
								IntProperty? uidProperty = (IntProperty?)((PropertiesStruct)buildingInstanceProperty.Value!).Properties.FirstOrDefault(p => p.Name.Equals("IcarusUID"))?.Property;
								if (uidProperty is not null)
								{
									uids.Add(uidProperty.Value);
								}
							}
						}
					}
				}
			}

			return uids;
		}

		private bool ListPlayers(ProspectSave prospect)
		{
			CharactersData characters = CharacterReader.ReadCharacters(prospect, mLogger, true);
			mLogger.Log(LogLevel.Information, $"Listing {characters.Characters.Count} characters");
			Table table = CreateTable("[deepskyblue1]PlayerID-CharacterSlot[/]", "[deepskyblue1]Character Name[/]", "[deepskyblue1]Mounts[/]", "[deepskyblue1]DropShip Location[/]");
			table.Title = new TableTitle("[yellow]Players[/]");
			foreach (CharacterData character in characters.Characters)
			{
				string dropShipLocation = "[No Rocket]";
				if (character.RocketRecorder.HasValue)
				{
					foreach (FPropertyTag prop in character.RocketRecorder.Value.Data)
					{
						if (prop.Name.Value.Equals("SpawnLocation", StringComparison.OrdinalIgnoreCase))
						{
							VectorStruct spawnLocationStruct = (VectorStruct)prop.Property!.Value!;
							dropShipLocation = $"{spawnLocationStruct.Value.X:0},{spawnLocationStruct.Value.Y:0}";
							break;
						}
					}
				}
				table.AddRow(EscapeMarkup(character.ID.ToString()), EscapeMarkup(character.Name ?? string.Empty), (character.OwnedMountRecorders?.Count ?? 0).ToString(), EscapeMarkup(dropShipLocation));
			}
			mLogger.Render(table);

			if (characters.UnownedPlayerStates is not null && characters.UnownedPlayerStates.Count > 0)
			{
				mLogger.Warning($"Found {characters.UnownedPlayerStates.Count} player states not associated with a valid player.");
				Table warningTable = CreateTable("[yellow]Player state ID[/]");
				foreach (RecorderData recorder in characters.UnownedPlayerStates)
				{
					FPropertyTag? idProperty = recorder.Data.FirstOrDefault(p => p.Name.Value.Equals("PlayerCharacterID", StringComparison.OrdinalIgnoreCase));
					CharacterID? id = idProperty is null ? null : CharacterReader.ReadCharacterID(idProperty.Property!);
					warningTable.AddRow(EscapeMarkup(id?.ToString() ?? "(No ID)"));
				}
				mLogger.Render(warningTable, LogLevel.Warning);
			}

			if (characters.UnownedRocketSpawns is not null && characters.UnownedRocketSpawns.Count > 0)
			{
				mLogger.Warning($"Found {characters.UnownedRocketSpawns.Count} rocket spawn actors not owned by any player.");
				Table warningTable = CreateTable("[yellow]Actor[/]", "[yellow]Location[/]");
				foreach (RecorderData recorder in characters.UnownedRocketSpawns)
				{
					int actorId = -1;
					FVector? actorLocation = null;
					foreach (FPropertyTag prop in recorder.Data)
					{
						if (prop.Name.Value.Equals("IcarusActorGUID", StringComparison.OrdinalIgnoreCase)) actorId = (int)prop.Property!.Value!;
						else if (prop.Name.Value.Equals("ActorTransform", StringComparison.OrdinalIgnoreCase))
						{
							PropertiesStruct transformStruct = (PropertiesStruct)prop.Property!.Value!;
							FPropertyTag translationProp = transformStruct.Properties.First(p => p.Name.Value.Equals("Translation", StringComparison.OrdinalIgnoreCase));
							VectorStruct locationStruct = (VectorStruct)translationProp.Property!.Value!;
							actorLocation = locationStruct.Value;
						}
					}
					warningTable.AddRow(actorId >= 0 ? actorId.ToString() : "No ID", EscapeMarkup(actorLocation.HasValue ? $"{actorLocation.Value.X:0},{actorLocation.Value.Y:0}" : "No Location"));
				}
				mLogger.Render(warningTable, LogLevel.Warning);
			}

			if (characters.UnownedRockets is not null && characters.UnownedRockets.Count > 0)
			{
				mLogger.Warning($"Found {characters.UnownedRockets.Count} rocket actors not owned by any player.");
				Table warningTable = CreateTable("[yellow]Actor[/]", "[yellow]Location[/]");
				foreach (RecorderData recorder in characters.UnownedRockets)
				{
					int actorId = -1;
					FVector? actorLocation = null;
					foreach (FPropertyTag prop in recorder.Data)
					{
						if (prop.Name.Value.Equals("IcarusActorGUID", StringComparison.OrdinalIgnoreCase)) actorId = (int)prop.Property!.Value!;
						else if (prop.Name.Value.Equals("SpawnLocation", StringComparison.OrdinalIgnoreCase)) actorLocation = ((VectorStruct)prop.Property!.Value!).Value;
					}
					warningTable.AddRow(actorId >= 0 ? actorId.ToString() : "No ID", EscapeMarkup(actorLocation.HasValue ? $"{actorLocation.Value.X:0},{actorLocation.Value.Y:0}" : "No Location"));
				}
				mLogger.Render(warningTable, LogLevel.Warning);
			}

			return true;
		}

		private bool CleanupUnassociatedRecorders(ProspectSave prospect)
		{
			mLogger.Log(LogLevel.Information, "Performing record cleanup");

			CharactersData characters = CharacterReader.ReadCharacters(prospect, mLogger, true);

			HashSet<int> recordersToRemove = new();

			if (characters.UnownedPlayerStates is not null && characters.UnownedPlayerStates.Count > 0)
			{
				mLogger.Log(LogLevel.Information, $"Removing {characters.UnownedPlayerStates.Count} unassociated player states");
				foreach (RecorderData recorder in characters.UnownedPlayerStates)
				{
					recordersToRemove.Add(recorder.Index);
				}
			}

			if (characters.UnownedRocketSpawns is not null && characters.UnownedRocketSpawns.Count > 0)
			{
				mLogger.Log(LogLevel.Information, $"Removing {characters.UnownedRocketSpawns.Count} unowned rocket spawns");
				foreach (RecorderData recorder in characters.UnownedRocketSpawns)
				{
					recordersToRemove.Add(recorder.Index);
				}
			}

			if (characters.UnownedRockets is not null && characters.UnownedRockets.Count > 0)
			{
				mLogger.Log(LogLevel.Information, $"Removing {characters.UnownedRockets.Count} unowned rockets...");
				foreach (RecorderData recorder in characters.UnownedRockets)
				{
					recordersToRemove.Add(recorder.Index);
				}
			}

			if (characters.UnownedGravestones is not null && characters.UnownedGravestones.Count > 0)
			{
				mLogger.Log(LogLevel.Information, $"Removing {characters.UnownedGravestones.Count} unowned player bodies...");
				foreach (RecorderData recorder in characters.UnownedGravestones)
				{
					recordersToRemove.Add(recorder.Index);
				}
			}

			if (recordersToRemove.Count == 0)
			{
				mLogger.Log(LogLevel.Information, "Found nothing to cleanup.");
				return true;
			}

			RemoveRecorders(prospect, recordersToRemove);

			return true;
		}

		private bool RemovePlayers(ProspectSave prospect, IReadOnlyList<string> playersToRemove)
		{
			List<CharacterID> inputCharacters = new();
			foreach (string input in playersToRemove)
			{
				if (!CharacterID.TryParse(input, out CharacterID characterId))
				{
					mLogger.Error($"Could not parse input as a character ID: {input}");
					return false;
				}
				inputCharacters.Add(characterId);
			}

			HashSet<CharacterData> charactersToRemove = new();

			CharactersData allCharacters = CharacterReader.ReadCharacters(prospect, mLogger);
			foreach (CharacterData character in allCharacters.Characters)
			{
				foreach (CharacterID inputCharacter in inputCharacters)
				{
					if (character.ID.Matches(inputCharacter))
					{
						charactersToRemove.Add(character);
					}
				}
			}

			if (charactersToRemove.Count == 0)
			{
				mLogger.Log(LogLevel.Information, "No players/characters matching the supplied IDs were found in the prospect.");
				return false;
			}

			mLogger.Log(LogLevel.Information, "Removing the following characters from the prospect:");
			foreach (CharacterData character in charactersToRemove)
			{
				mLogger.Log(LogLevel.Information, $"  {character.ToString()}");
			}

			// Remove from json associated members
			for (int i = prospect.ProspectInfo.AssociatedMembers.Count - 1; i >= 0; --i)
			{
				FAssociatedMember member = prospect.ProspectInfo.AssociatedMembers[i];
				CharacterID id = new(member.UserID, member.ChrSlot);
				foreach (CharacterData removeCharacter in charactersToRemove)
				{
					if (removeCharacter.ID.Matches(id))
					{
						prospect.ProspectInfo.AssociatedMembers.RemoveAt(i);
						break;
					}
				}
			}

			// Remove from binary associated members
			{
				ArrayProperty? associatedMembersProperty = GetProspectInfoProperty<ArrayProperty>(prospect, nameof(FProspectInfo.AssociatedMembers));
				if (associatedMembersProperty is not null)
				{
					List<FProperty> membersToKeep = new();
					foreach (FProperty memberProp in associatedMembersProperty.Value!)
					{
						bool keep = true;
						CharacterID? id = CharacterReader.ReadCharacterID(memberProp);
						if (id.HasValue)
						{
							foreach (CharacterData removeCharacter in charactersToRemove)
							{
								if (removeCharacter.ID.Matches(id.Value))
								{
									keep = false;
									break;
								}
							}
						}
						if (keep)
						{
							membersToKeep.Add(memberProp);
						}
					}
					associatedMembersProperty.Value = membersToKeep.ToArray();
				}
			}

			// Unclaim mounts
			foreach (CharacterData removeCharacter in charactersToRemove)
			{
				if (removeCharacter.OwnedMountRecorders is null) continue;

				foreach (RecorderData mountRecorder in removeCharacter.OwnedMountRecorders)
				{
					foreach (FPropertyTag prop in mountRecorder.Data)
					{
						if (prop.Name.Value.Equals("OwnerCharacterID", StringComparison.OrdinalIgnoreCase))
						{
							CharacterReader.UpdateCharacterID(prop.Property!, CharacterID.Null);
						}
						else if (prop.Name.Value.Equals("OwnerName", StringComparison.OrdinalIgnoreCase))
						{
							prop.Property!.Value = null;
						}
					}

					mountRecorder.Serialize(prospect);
				}
			}

			// Remove from player history
			HashSet<int> historyIndicesToRemove = charactersToRemove.Select(c => c.HistoryIndex).ToHashSet();
			foreach (FPropertyTag prop in allCharacters.PlayerHistoryRecorder.Data)
			{
				if (prop.Name.Value.Equals("SavedHistoryData"))
				{
					ArrayProperty savedHistoryProperty = (ArrayProperty)prop.Property!;
					List<FProperty> historyToKeep = new();
					for (int i = 0; i < savedHistoryProperty.Value!.Length; ++i)
					{
						if (historyIndicesToRemove.Contains(i)) continue;
						historyToKeep.Add(((FProperty[])savedHistoryProperty.Value)[i]);
					}
					savedHistoryProperty.Value = historyToKeep.ToArray();

					break;
				}
			}
			allCharacters.PlayerHistoryRecorder.Serialize(prospect);

			// Remove recorders
			HashSet<int> recordersToRemove = new();
			foreach (CharacterData character in charactersToRemove)
			{
				recordersToRemove.Add(character.PlayerRecorder.Index);
				if (character.PlayerStateRecorder.HasValue)
				{
					recordersToRemove.Add(character.PlayerStateRecorder.Value.Index);
				}
				if (character.RocketSpawnRecorder.HasValue)
				{
					recordersToRemove.Add(character.RocketSpawnRecorder.Value.Index);
				}
				if (character.RocketRecorder.HasValue)
				{
					recordersToRemove.Add(character.RocketRecorder.Value.Index);
				}
				if (character.GravestoneRecorder.HasValue)
				{
					recordersToRemove.Add(character.GravestoneRecorder.Value.Index);
				}
			}
			RemoveRecorders(prospect, recordersToRemove);

			return true;
		}

		private PropertiesStruct? GetProspectInfo(ProspectSave prospect)
		{
			StructProperty? prospectInfoProperty = prospect.ProspectData.FirstOrDefault(p => p.Name.Equals("ProspectInfo"))?.Property as StructProperty;
			if (prospectInfoProperty is null)
			{
				mLogger.Error("Error locating prospect info property inside binary blob");
				return null;
			}

			PropertiesStruct? prospectInfoPropertyData = prospectInfoProperty.Value as PropertiesStruct;
			if (prospectInfoPropertyData is null)
			{
				mLogger.Error("Error reading prospect info property inside binary blob");
				return null;
			}

			return prospectInfoPropertyData;
		}

		private T? GetProspectInfoProperty<T>(ProspectSave prospect, string propertyName) where T : FProperty
		{
			PropertiesStruct? prospectInfo = GetProspectInfo(prospect);
			if (prospectInfo is null) return null;

			T? property = prospectInfo.Properties.FirstOrDefault(p => p.Name.Equals(propertyName))?.Property as T;
			if (property is null)
			{
				mLogger.Error($"Error locating property '{propertyName}' inside binary blob");
			}
			return property;
		}

		private static string GetEnumValue(string? enumPair, string defaultValue)
		{
			if (enumPair is null) return defaultValue;

			int separatorIndex = enumPair.LastIndexOf("::");
			if (separatorIndex >= 0)
			{
				return enumPair[(separatorIndex + 2)..];
			}

			return enumPair;
		}

		private static void RemoveRecorders(ProspectSave prospect, IReadOnlySet<int> recordersToRemove)
		{
			ArrayProperty recorderProperties = (ArrayProperty)prospect.ProspectData[0].Property!;
			List<FProperty> propsToKeep = new();
			for (int i = 0; i < recorderProperties.Value!.Length; ++i)
			{
				if (recordersToRemove.Contains(i)) continue;
				propsToKeep.Add(((FProperty[])recorderProperties.Value)[i]);
			}
			recorderProperties.Value = propsToKeep.ToArray();
		}

		private static string FormatTimestamp(int value, bool includeDays = true)
		{
			int seconds = value % 60;
			int minutes = value / 60 % 60;
			int hours;

			if (includeDays)
			{
				hours = value / 3600 % 24;
				int days = value / 3600 / 24;

				return $"{days}:{hours:00}:{minutes:00}:{seconds:00}";
			}

			hours = value / 3600;
			return $"{hours:00}:{minutes:00}:{seconds:00}";
		}

		private static string? FormatVector(FVector? value)
		{
			if (!value.HasValue) return null;

			return $"{Math.Round(value.Value.X)}, {Math.Round(value.Value.Y)}, {Math.Round(value.Value.Z)}";
		}

		private static Table CreateTable(params string[] columns)
		{
			Table table = new();
			table.Border = TableBorder.Rounded;
			table.BorderColor(Color.Grey);
			table.Expand();
			foreach (string column in columns)
			{
				table.AddColumn(column);
			}
			return table;
		}

		private static string EscapeMarkup(string? value)
		{
			return Markup.Escape(value ?? string.Empty);
		}

		private static string FormatRecorderName(string value)
		{
			return value.StartsWith("/Script/Icarus.") ? value.Substring(15) : value;
		}

		private enum EMissionState
		{
			InProgress,
			Completed,
			Abandoned,
			Failed,
			MAX,
		};
	}

	#endregion

	#region Program.cs

	internal class Program
	{
		private const string MenuBack = "Back";
		private const string MenuExit = "Exit";

		private static int Main(string[] args)
		{
			Logger? logger;
			if (!TryCreateLoggger(out logger))
			{
				Console.Error.WriteLine("No logger could be created. Program will exit.");
				return OnExit(1);
			}

			bool success = args.Length == 0
				? RunInteractive(logger)
				: RunCommandLine(args, logger);

			return OnExit(success ? 0 : 1);
		}

		private static bool RunCommandLine(IReadOnlyList<string> args, Logger logger)
		{
			ProgramOptions? options;
			if (!ProgramOptions.TryParseCommandLine(args, logger, out options))
			{
				PrintUsage(logger, LogLevel.Warning);
				return false;
			}

			if (!options.Any())
			{
				PrintUsage(logger);
				return true;
			}

			return ExecuteOptions(options, logger);
		}

		private static bool RunInteractive(Logger logger)
		{
			try
			{
				AnsiConsole.Write(new FigletText("EditIcarusProspect").Color(Color.CornflowerBlue));
				logger.Important("Interactive mode launched. Command-line parameters remain available for scripting.");

				string? prospectPath = null;
				while (true)
				{
					string title = prospectPath is null
						? "[green]Select an action[/]"
						: $"[green]Select an action[/] ([grey]{Markup.Escape(prospectPath)}[/])";

					string action = AnsiConsole.Prompt(
						new SelectionPrompt<string>()
							.Title(title)
							.PageSize(10)
							.AddChoices(
								"Load prospect file",
								"View prospect statistics",
								"Modify prospect properties",
								"Manage mission history",
								"Manage prebuilt structures",
								"List/manage players",
								"Perform cleanup operations",
								"Show command-line usage",
								MenuExit));

					switch (action)
					{
						case "Load prospect file":
							prospectPath = PromptForProspectPath(prospectPath);
							logger.Information($"Selected prospect: {prospectPath}");
							break;
						case "View prospect statistics":
							if (EnsureProspectPath(ref prospectPath))
							{
								ExecuteOptions(new ProgramOptions(prospectPath!, true, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, null, false, false, null), logger);
							}
							break;
						case "Modify prospect properties":
							if (EnsureProspectPath(ref prospectPath))
							{
								prospectPath = RunPropertyMenu(prospectPath!, logger);
							}
							break;
						case "Manage mission history":
							if (EnsureProspectPath(ref prospectPath))
							{
								RunMissionMenu(prospectPath!, logger);
							}
							break;
						case "Manage prebuilt structures":
							if (EnsureProspectPath(ref prospectPath))
							{
								RunPrebuiltMenu(prospectPath!, logger);
							}
							break;
						case "List/manage players":
							if (EnsureProspectPath(ref prospectPath))
							{
								RunPlayerMenu(prospectPath!, logger);
							}
							break;
						case "Perform cleanup operations":
							if (EnsureProspectPath(ref prospectPath) && AnsiConsole.Confirm("Run cleanup on unassociated player recorders?"))
							{
								ExecuteOptions(new ProgramOptions(prospectPath!, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, null, false, true, null), logger);
							}
							break;
						case "Show command-line usage":
							PrintUsage(logger);
							break;
						case MenuExit:
							return true;
					}
				}
			}
			catch (NotSupportedException)
			{
				logger.Warning("Interactive prompts are unavailable in this terminal. Showing command-line usage instead.");
				PrintUsage(logger);
				return true;
			}
		}

		private static string RunPropertyMenu(string prospectPath, Logger logger)
		{
			while (true)
			{
				string action = AnsiConsole.Prompt(
					new SelectionPrompt<string>()
						.Title("[green]Modify prospect properties[/]")
						.AddChoices(
							"Rename prospect",
							"Set difficulty",
							"Set privacy",
							"Set hardcore mode",
							"Set drop zone",
							MenuBack));

				switch (action)
				{
					case "Rename prospect":
						string newName = AnsiConsole.Prompt(
							new TextPrompt<string>("[green]New prospect name[/]")
								.PromptStyle("green")
								.Validate(input => string.IsNullOrWhiteSpace(input) ? ValidationResult.Error("[red]Name is required[/]") : ValidationResult.Success()));
						if (ExecuteOptions(new ProgramOptions(prospectPath, false, newName, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, null, false, false, null), logger))
						{
							prospectPath = Path.Combine(Path.GetDirectoryName(prospectPath)!, $"{newName}.json");
						}
						break;
					case "Set difficulty":
						EMissionDifficulty difficulty = AnsiConsole.Prompt(new SelectionPrompt<EMissionDifficulty>().Title("[green]Select difficulty[/]").AddChoices(EMissionDifficulty.Easy, EMissionDifficulty.Medium, EMissionDifficulty.Hard, EMissionDifficulty.Extreme));
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, difficulty, null, null, null, null, false, false, null), logger);
						break;
					case "Set privacy":
						ELobbyPrivacy privacy = AnsiConsole.Prompt(new SelectionPrompt<ELobbyPrivacy>().Title("[green]Select privacy[/]").AddChoices(ELobbyPrivacy.FriendsOnly, ELobbyPrivacy.Private));
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, privacy, EMissionDifficulty.None, null, null, null, null, false, false, null), logger);
						break;
					case "Set hardcore mode":
						bool hardcore = AnsiConsole.Confirm("Enable hardcore (no self-respawn)?");
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, hardcore, null, null, null, false, false, null), logger);
						break;
					case "Set drop zone":
						int dropZone = AnsiConsole.Prompt(new TextPrompt<int>("[green]Drop zone index[/]").PromptStyle("green"));
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, dropZone, null, null, false, false, null), logger);
						break;
					case MenuBack:
						return prospectPath;
				}
			}
		}

		private static void RunMissionMenu(string prospectPath, Logger logger)
		{
			while (true)
			{
				string action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]Manage mission history[/]").AddChoices("List missions", "Remove missions", "Clear missions", MenuBack));
				switch (action)
				{
					case "List missions":
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, new MissionOptions { Command = MissionCommand.List, Parameters = Array.Empty<int>() }, null, false, false, null), logger);
						break;
					case "Remove missions":
						int[] missionIndices = PromptForIntegerList("[green]Mission indices to remove (comma-separated)[/]");
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, new MissionOptions { Command = MissionCommand.Remove, Parameters = missionIndices }, null, false, false, null), logger);
						break;
					case "Clear missions":
						if (AnsiConsole.Confirm("Remove all recorded missions?"))
						{
							ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, new MissionOptions { Command = MissionCommand.Clear, Parameters = Array.Empty<int>() }, null, false, false, null), logger);
						}
						break;
					case MenuBack:
						return;
				}
			}
		}

		private static void RunPrebuiltMenu(string prospectPath, Logger logger)
		{
			while (true)
			{
				string action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]Manage prebuilt structures[/]").AddChoices("List prebuilts", "Show prebuilt details", "Remove prebuilts", "Clear prebuilts", MenuBack));
				switch (action)
				{
					case "List prebuilts":
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, new PrebuiltOptions { Command = PrebuiltCommand.List, Parameters = Array.Empty<int>() }, false, false, null), logger);
						break;
					case "Show prebuilt details":
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, new PrebuiltOptions { Command = PrebuiltCommand.Details, Parameters = Array.Empty<int>() }, false, false, null), logger);
						break;
					case "Remove prebuilts":
						int[] prebuiltIndices = PromptForIntegerList("[green]Prebuilt indices to remove (comma-separated)[/]");
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, new PrebuiltOptions { Command = PrebuiltCommand.Remove, Parameters = prebuiltIndices }, false, false, null), logger);
						break;
					case "Clear prebuilts":
						if (AnsiConsole.Confirm("Remove all mission-generated prebuilt structures?"))
						{
							ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, new PrebuiltOptions { Command = PrebuiltCommand.Clear, Parameters = Array.Empty<int>() }, false, false, null), logger);
						}
						break;
					case MenuBack:
						return;
				}
			}
		}

		private static void RunPlayerMenu(string prospectPath, Logger logger)
		{
			while (true)
			{
				string action = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("[green]List/manage players[/]").AddChoices("List players", "Remove players", MenuBack));
				switch (action)
				{
					case "List players":
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, null, true, false, null), logger);
						break;
					case "Remove players":
						List<string> players = PromptForPlayerList();
						ExecuteOptions(new ProgramOptions(prospectPath, false, null, ELobbyPrivacy.Unknown, EMissionDifficulty.None, null, null, null, null, false, false, players), logger);
						break;
					case MenuBack:
						return;
				}
			}
		}

		private static bool EnsureProspectPath(ref string? prospectPath)
		{
			if (!string.IsNullOrWhiteSpace(prospectPath) && File.Exists(prospectPath))
			{
				return true;
			}

			prospectPath = PromptForProspectPath(prospectPath);
			return true;
		}

		private static string PromptForProspectPath(string? currentPath)
		{
			TextPrompt<string> prompt = new TextPrompt<string>("[green]Prospect save path[/]").PromptStyle("green");
			if (!string.IsNullOrWhiteSpace(currentPath))
			{
				prompt.DefaultValue(currentPath);
			}

			prompt.Validate(input =>
			{
				string fullPath = Path.GetFullPath(input);
				return File.Exists(fullPath)
					? ValidationResult.Success()
					: ValidationResult.Error($"[red]File not found:[/] {Markup.Escape(fullPath)}");
			});

			return Path.GetFullPath(AnsiConsole.Prompt(prompt));
		}

		private static int[] PromptForIntegerList(string title)
		{
			TextPrompt<string> prompt = new TextPrompt<string>(title).PromptStyle("green");
			prompt.Validate(input => TryParseIntegerList(input, out _) ? ValidationResult.Success() : ValidationResult.Error("[red]Enter one or more comma-separated integers[/]"));
			return TryParseIntegerList(AnsiConsole.Prompt(prompt), out int[]? values) ? values : Array.Empty<int>();
		}

		private static List<string> PromptForPlayerList()
		{
			TextPrompt<string> prompt = new TextPrompt<string>("[green]Players/characters to remove (comma-separated)[/]").PromptStyle("green");
			prompt.Validate(input =>
			{
				string[] candidates = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (candidates.Length == 0)
				{
					return ValidationResult.Error("[red]Enter at least one player or character ID[/]");
				}

				return candidates.All(candidate => CharacterID.TryParse(candidate, out _))
					? ValidationResult.Success()
					: ValidationResult.Error("[red]Use SteamID or SteamID-slot format[/]");
			});

			return AnsiConsole.Prompt(prompt)
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(item => item.ToLowerInvariant())
				.ToList();
		}

		private static bool TryParseIntegerList(string input, [NotNullWhen(true)] out int[]? values)
		{
			string[] parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length == 0)
			{
				values = null;
				return false;
			}

			values = new int[parts.Length];
			for (int i = 0; i < parts.Length; ++i)
			{
				if (!int.TryParse(parts[i], out values[i]))
				{
					values = null;
					return false;
				}
			}

			return true;
		}

		private static bool ExecuteOptions(ProgramOptions options, Logger logger)
		{
			if (!File.Exists(options.ProspectPath))
			{
				logger.Error($"File not found or not accessible: {options.ProspectPath}");
				return false;
			}

			bool success;
			try
			{
				success = UpdateProspect(options, logger);
			}
			catch (Exception ex)
			{
				logger.Error($"{ex.GetType().FullName}: {ex.Message}");
				return false;
			}

			if (success)
			{
				logger.Important("Done.");
			}

			return success;
		}

		private static bool TryCreateLoggger([NotNullWhen(true)] out Logger? logger)
		{
			logger = null;
			try
			{
				logger = ConsoleLogger.Create(Encoding.UTF8);
				return true;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to create UTF8 logger. Error: [{ex.GetType().FullName}] {ex.Message}");
			}

			if (logger is null)
			{
				try
				{
					logger = new Logger();
					return true;
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine($"Failed to create default logger. Error: [{ex.GetType().FullName}] {ex.Message}");
				}
			}

			return false;
		}

		private static int OnExit(int code)
		{
			if (System.Diagnostics.Debugger.IsAttached)
			{
				Console.ReadKey(true);
			}
			return code;
		}

		private static void PrintUsage(Logger logger, LogLevel logLevel = LogLevel.Information)
		{
			logger.Render(new Rule("[yellow]Command-line usage[/]"));
			logger.Log(logLevel, "Run without parameters to launch interactive mode.");
			logger.LogEmptyLine(logLevel);

			string optionIndent = "    ";
			logger.Log(logLevel, "Usage: EditIcarusProspect [options] path");
			logger.LogEmptyLine(logLevel);
			ProgramOptions.PrintCommandLineOptions(logger, indent: optionIndent);
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, $"{optionIndent}{"path",-ProgramOptions.MaxOptionStringLength}  The path to the prospect save json file to modify. Recommended to backup file first.");
			logger.LogEmptyLine(logLevel);
			logger.Log(logLevel, "Note: Must select at least one action to perform.");
		}

		private static bool UpdateProspect(ProgramOptions options, Logger logger)
		{
			string path = options.ProspectPath;
			string oldPath = path;
			if (options.ProspectName is not null)
			{
				path = Path.Combine(Path.GetDirectoryName(path)!, $"{options.ProspectName}.json");
				if (File.Exists(path))
				{
					logger.Error($"Cannot rename prospect. A prospect with the name {options.ProspectName} already exists.");
					return false;
				}
			}

			ProspectSave? prospect;
			try
			{
				prospect = RunWithProgress("Loading prospect", () =>
				{
					using FileStream file = File.OpenRead(oldPath);
					return ProspectSave.Load(file);
				});
			}
			catch (Exception ex)
			{
				logger.Error($"Error reading prospect file. [{ex.GetType().FullName}] {ex.Message}");
				return false;
			}

			if (prospect == null)
			{
				logger.Error("Error reading prospect file. Could not load Json.");
				return false;
			}

			ProspectEditor editor = new(logger);
			bool shouldSave = RunWithProgress("Processing prospect", () => editor.Run(prospect, options));
			if (!shouldSave)
			{
				return true;
			}

			RunWithProgress("Saving prospect", () =>
			{
				using FileStream file = File.Create(path);
				prospect.Save(file);
				return true;
			});

			if (options.ProspectName is not null)
			{
				File.Delete(oldPath);
			}

			return true;
		}

		private static T RunWithProgress<T>(string description, Func<T> action)
		{
			T? result = default;
			Exception? error = null;

			AnsiConsole.Progress()
				.AutoClear(true)
				.Columns(
					new TaskDescriptionColumn(),
					new ProgressBarColumn(),
					new PercentageColumn(),
					new SpinnerColumn(),
					new ElapsedTimeColumn())
				.Start(ctx =>
				{
					ProgressTask task = ctx.AddTask($"[yellow]{Markup.Escape(description)}[/]");
					task.IsIndeterminate = true;
					try
					{
						result = action();
					}
					catch (Exception ex)
					{
						error = ex;
					}
					finally
					{
						task.IsIndeterminate = false;
						task.Value = task.MaxValue;
					}
				});

			if (error is not null)
			{
				throw error;
			}

			return result!;
		}
	}

	#endregion

}
