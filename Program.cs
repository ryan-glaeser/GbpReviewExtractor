using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SerpApi;

namespace GbpReviewExtractor
{
    class Program
    {
        private static string _serpApiKey = string.Empty;
        private static string _googleMapsDataId = string.Empty;

        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 Google Business Public Review Extractor (No-Auth Engine)\n");

            // Load the environment variables from the local .env file
            try
            {
                DotNetEnv.Env.Load();

                _serpApiKey = Environment.GetEnvironmentVariable("SERP_API_KEY") ?? string.Empty;
                _googleMapsDataId = Environment.GetEnvironmentVariable("GOOGLE_MAPS_DATA_ID") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(_serpApiKey) || string.IsNullOrWhiteSpace(_googleMapsDataId))
                {
                    Console.WriteLine("❌ Configuration Error: SERP_API_KEY or GOOGLE_MAPS_DATA_ID is missing from your .env file.");
                    Console.WriteLine("Please ensure a valid '.env' file is present in the application directory.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize .env profile loader: {ex.Message}");
                return;
            }

            // Prompt user for target month input
            Console.Write("Enter target month for review collection (YYYY-MM): ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (!DateTime.TryParseExact(input, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime targetMonthStart))
            {
                Console.WriteLine("❌ Invalid format. Please run the program again and use YYYY-MM (e.g., 2026-06).");
                return;
            }

            // Calculate exact target window boundaries
            DateTime targetMonthEnd = targetMonthStart.AddMonths(1).AddTicks(-1);
            Console.WriteLine($"\n[Configuration] Target Window: {targetMonthStart:yyyy-MM-dd} to {targetMonthEnd:yyyy-MM-dd}");

            string csvFilename = $"Google_Reviews_{targetMonthStart:yyyyMM}.csv";

            try
            {
                // CROSS-PLATFORM TIMEZONE CONFIGURATION
                TimeZoneInfo pacificZone;
                try
                {
                    pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");
                }
                catch (TimeZoneNotFoundException)
                {
                    pacificZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
                }

                using (var writer = new StreamWriter(csvFilename, false, Encoding.UTF8))
                {
                    // Set up explicit tracking sheet schema layout
                    await writer.WriteLineAsync("Source,Guest Name,Review Date,Server Name,Overall Rating,Food,Service,Atmosphere,Review Comments,Restaurant Reply");

                    Hashtable searchParams = new Hashtable();
                    searchParams.Add("engine", "google_maps_reviews");
                    searchParams.Add("data_id", _googleMapsDataId); // Loaded from .env
                    searchParams.Add("sort_by", "newestFirst");

                    Console.WriteLine("[Scraping] Downloading public reviews from Google Maps...");

                    int totalSaved = 0;
                    int skippedNewer = 0;
                    int consecutiveOlderCount = 0;
                    const int MaxConsecutiveOlderAllowed = 20;
                    bool keepReading = true;

                    while (keepReading)
                    {
                        GoogleSearch jb = new GoogleSearch(searchParams, _serpApiKey); // Loaded from .env
                        JObject result = jb.GetJson();

                        var reviews = result["reviews"];
                        if (reviews != null)
                        {
                            foreach (var review in reviews)
                            {
                                string isoDateStr = review["iso_date"]?.ToString() ?? "";

                                if (DateTime.TryParse(isoDateStr, out DateTime utcDate))
                                {
                                    utcDate = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
                                    DateTime reviewPacificTime = TimeZoneInfo.ConvertTimeFromUtc(utcDate, pacificZone);

                                    // Scenario A: Review is newer than our target month window
                                    if (reviewPacificTime > targetMonthEnd)
                                    {
                                        skippedNewer++;
                                        consecutiveOlderCount = 0;
                                        continue;
                                    }

                                    // Scenario B: Review is older than our target month window
                                    if (reviewPacificTime < targetMonthStart)
                                    {
                                        consecutiveOlderCount++;
                                        if (consecutiveOlderCount >= MaxConsecutiveOlderAllowed)
                                        {
                                            Console.WriteLine($"\n[Boundary Reached] Encountered {consecutiveOlderCount} consecutive older reviews in a row. Safely stopping pipeline.");
                                            keepReading = false;
                                            break;
                                        }
                                        continue;
                                    }

                                    // Scenario C: Review is exactly inside our target month window
                                    consecutiveOlderCount = 0;

                                    string sourceValue = "Google";
                                    string serverNamePlaceholder = "";
                                    string formattedPacificDate = reviewPacificTime.ToString("yyyy-MM-dd");

                                    string guestName = EscapeForCsv(review["user"]?["name"]?.ToString());
                                    string overallRating = review["rating"]?.ToString() ?? "0";
                                    string reviewComments = EscapeForCsv(review["snippet"]?.ToString());

                                    string rawResponse = "";
                                    if (review["response"] != null && review["response"]["snippet"] != null)
                                    {
                                        rawResponse = review["response"]["snippet"]?.ToString() ?? "";
                                    }
                                    else if (review["response_from_owner"] != null && review["response_from_owner"]["snippet"] != null)
                                    {
                                        rawResponse = review["response_from_owner"]["snippet"]?.ToString() ?? "";
                                    }
                                    string restaurantReply = EscapeForCsv(rawResponse);

                                    var details = review["details"];
                                    string foodRating = details?["food"]?.ToString() ?? "";
                                    string serviceRating = details?["service"]?.ToString() ?? "";
                                    string atmosphereRating = details?["atmosphere"]?.ToString() ?? "";

                                    await writer.WriteLineAsync($"{sourceValue},{guestName},{formattedPacificDate},{serverNamePlaceholder},{overallRating},{foodRating},{serviceRating},{atmosphereRating},{reviewComments},{restaurantReply}");
                                    totalSaved++;
                                }
                            }
                        }

                        if (!keepReading) break;

                        var nextPageToken = result["serpapi_pagination"]?["next_page_token"]?.ToString();
                        if (!string.IsNullOrEmpty(nextPageToken))
                        {
                            if (searchParams.ContainsKey("next_page_token"))
                                searchParams["next_page_token"] = nextPageToken;
                            else
                                searchParams.Add("next_page_token", nextPageToken);

                            Console.WriteLine($"[Pagination] Moving to next page... (Saved: {totalSaved})");
                        }
                        else
                        {
                            keepReading = false;
                        }
                    }

                    Console.WriteLine($"\n[Success] Data extraction pipeline complete!");
                    Console.WriteLine($"📝 Total Rows Saved for {input}: {totalSaved}");
                    Console.WriteLine($"📂 File Saved To: {Path.GetFullPath(csvFilename)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Critical Runtime Error: {ex.Message}");
            }
        }

        private static string EscapeForCsv(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Contains(",") || text.Contains("\"") || text.Contains("\n") || text.Contains("\r"))
            {
                return $"\"{text.Replace("\"", "\"\"")}\"";
            }
            return text;
        }
    }
}