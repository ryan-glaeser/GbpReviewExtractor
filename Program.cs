using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SerpApi;

namespace GbpReviewExtractor
{
    public class ReviewRecord
    {
        public string Source { get; set; } = string.Empty;
        public string GuestName { get; set; } = string.Empty;
        public DateTime ReviewDate { get; set; }
        public string ServerName { get; set; } = string.Empty;
        public string OverallRating { get; set; } = "0";
        public string FoodRating { get; set; } = string.Empty;
        public string ServiceRating { get; set; } = string.Empty;
        public string AtmosphereRating { get; set; } = string.Empty;
        public string ReviewComments { get; set; } = string.Empty;
        public string RestaurantReply { get; set; } = string.Empty;
    }

    class Program
    {
        private static string _serpApiKey = string.Empty;
        private static string _googleMapsDataId = string.Empty;
        private static string _yelpPlaceId = string.Empty;
        private static string _tripAdvisorLocationId = string.Empty;

        static async Task Main(string[] args)
        {
            Console.WriteLine("🚀 Multi-Platform Public Review Extractor (Google, Yelp, TripAdvisor)\n");

            try
            {
                string baseDir = AppContext.BaseDirectory;
                DotNetEnv.Env.Load(Path.Combine(baseDir, "settings.env"));

                _serpApiKey = Environment.GetEnvironmentVariable("SERP_API_KEY") ?? string.Empty;
                _googleMapsDataId = Environment.GetEnvironmentVariable("GOOGLE_MAPS_DATA_ID") ?? string.Empty;
                _yelpPlaceId = Environment.GetEnvironmentVariable("YELP_PLACE_ID") ?? string.Empty;
                _tripAdvisorLocationId = Environment.GetEnvironmentVariable("TRIPADVISOR_LOCATION_ID") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(_serpApiKey))
                {
                    Console.WriteLine("❌ Configuration Error: SERP_API_KEY is missing from your .env file.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to initialize .env profile loader: {ex.Message}");
                return;
            }

            Console.Write("Enter target month for review collection (YYYY-MM): ");
            string input = Console.ReadLine()?.Trim() ?? "";

            if (!DateTime.TryParseExact(input, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime targetMonthStart))
            {
                Console.WriteLine("❌ Invalid format. Use YYYY-MM (e.g., 2026-06).");
                return;
            }

            DateTime targetMonthEnd = targetMonthStart.AddMonths(1).AddTicks(-1);
            Console.WriteLine($"\n[Configuration] Target Window: {targetMonthStart:yyyy-MM-dd} to {targetMonthEnd:yyyy-MM-dd}");

            TimeZoneInfo pacificZone;
            try
            {
                pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Vancouver");
            }
            catch (TimeZoneNotFoundException)
            {
                pacificZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            }

            var masterReviewList = new List<ReviewRecord>();

            try
            {
                // 1. GOOGLE MAPS
                if (!string.IsNullOrWhiteSpace(_googleMapsDataId))
                {
                    Console.WriteLine("\n[Scraping] Fetching Google Maps reviews...");
                    var googleReviews = await FetchGoogleReviewsAsync(targetMonthStart, targetMonthEnd, pacificZone);
                    masterReviewList.AddRange(googleReviews);
                    Console.WriteLine($"✅ Google Maps: Saved {googleReviews.Count} reviews.");
                }

                // 2. YELP (Quota-Protected: Max 1-2 API calls)
                if (!string.IsNullOrWhiteSpace(_yelpPlaceId))
                {
                    Console.WriteLine("\n[Scraping] Fetching Yelp reviews (Quota-Protected)...");
                    var yelpReviews = await FetchYelpReviewsAsync(targetMonthStart, targetMonthEnd, pacificZone);
                    masterReviewList.AddRange(yelpReviews);
                    Console.WriteLine($"✅ Yelp: Saved {yelpReviews.Count} reviews.");
                }

                // 3. TRIPADVISOR (Quota-Protected: Max 1 API call)
                if (!string.IsNullOrWhiteSpace(_tripAdvisorLocationId))
                {
                    Console.WriteLine("\n[Scraping] Fetching TripAdvisor reviews (Quota-Protected)...");
                    var tripAdvisorReviews = await FetchTripAdvisorReviewsAsync(targetMonthStart, targetMonthEnd, pacificZone);
                    masterReviewList.AddRange(tripAdvisorReviews);
                    Console.WriteLine($"✅ TripAdvisor: Saved {tripAdvisorReviews.Count} reviews.");
                }

                // 4. EXPORT TO CSV
                string outputDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string csvPath = Path.Combine(outputDir, $"MultiPlatform_Reviews_{targetMonthStart:yyyyMM}.csv");

                await ExportToCsvAsync(masterReviewList, csvPath);

                Console.WriteLine($"\n[Success] Data extraction complete!");
                Console.WriteLine($"📝 Total Combined Rows Saved: {masterReviewList.Count}");
                Console.WriteLine($"📂 Output File: {csvPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Critical Runtime Error: {ex.Message}");
            }
        }

        // --- GOOGLE MAPS EXTRACTOR ---
        private static async Task<List<ReviewRecord>> FetchGoogleReviewsAsync(DateTime startDate, DateTime endDate, TimeZoneInfo pacificZone)
        {
            var allFetchedReviews = new List<ReviewRecord>();

            if (string.IsNullOrWhiteSpace(_googleMapsDataId))
            {
                Console.WriteLine("⚠️ GOOGLE_MAPS_DATA_ID is not configured.");
                return allFetchedReviews;
            }

            // Page 1: Initial search parameters using data_id + sort_by
            Hashtable searchParams = new Hashtable
    {
        { "engine", "google_maps_reviews" },
        { "data_id", _googleMapsDataId },
        { "sort_by", "newestFirst" }
    };

            const int MaxPagesAllowed = 25; // Increased page limit to ensure deep review capture
            int currentPage = 1;

            while (currentPage <= MaxPagesAllowed)
            {
                Console.WriteLine($"   Fetching Google page {currentPage} of max {MaxPagesAllowed}...");

                GoogleSearch jb = new GoogleSearch(searchParams, _serpApiKey);
                JObject result = jb.GetJson();

                if (result["error"] != null)
                {
                    Console.WriteLine($"⚠️ Google Maps API Error: {result["error"]}");
                    break;
                }

                var reviews = result["reviews"];

                if (reviews != null && reviews.HasValues)
                {
                    int parsedOnPage = 0;

                    foreach (var review in reviews)
                    {
                        string isoDateStr = review["iso_date"]?.ToString() ?? "";
                        string relativeDateStr = review["date"]?.ToString() ?? "";
                        string reviewerName = review["user"]?["name"]?.ToString() ?? "";
                        string snippet = review["snippet"]?.ToString() ?? "";

                        if (string.IsNullOrEmpty(reviewerName) && string.IsNullOrEmpty(snippet) && string.IsNullOrEmpty(isoDateStr))
                        {
                            continue;
                        }

                        DateTime reviewPacificTime;

                        if (DateTime.TryParse(isoDateStr, out DateTime utcDate))
                        {
                            utcDate = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
                            reviewPacificTime = TimeZoneInfo.ConvertTimeFromUtc(utcDate, pacificZone);
                        }
                        else if (!string.IsNullOrEmpty(relativeDateStr))
                        {
                            reviewPacificTime = ParseRelativeDateToPacific(relativeDateStr, pacificZone);
                        }
                        else
                        {
                            continue;
                        }

                        parsedOnPage++;

                        string rawResponse = review["response"]?["snippet"]?.ToString()
                                          ?? review["response_from_owner"]?["snippet"]?.ToString()
                                          ?? "";

                        allFetchedReviews.Add(new ReviewRecord
                        {
                            Source = "Google",
                            GuestName = reviewerName,
                            ReviewDate = reviewPacificTime,
                            OverallRating = review["rating"]?.ToString() ?? "0",
                            FoodRating = review["details"]?["food"]?.ToString() ?? "",
                            ServiceRating = review["details"]?["service"]?.ToString() ?? "",
                            AtmosphereRating = review["details"]?["atmosphere"]?.ToString() ?? "",
                            ReviewComments = snippet,
                            RestaurantReply = rawResponse
                        });
                    }

                    Console.WriteLine($"   Page {currentPage} finished. Processed {parsedOnPage} raw reviews.");
                }
                else
                {
                    Console.WriteLine("   No reviews found on this page.");
                    break;
                }

                // --- EXTRACT NEXT PAGE PARAMETERS DIRECTLY FROM SERPAPI'S NEXT URL ---
                var serpApiPagination = result["serpapi_pagination"];
                string nextUrl = serpApiPagination?["next"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(nextUrl))
                {
                    Console.WriteLine($"   [Pagination] Advancing to Page {currentPage + 1} using SerpApi next link...");

                    // Parse all query parameters provided directly in the 'next' URL from SerpApi
                    var uri = new Uri(nextUrl);
                    var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);

                    Hashtable nextSearchParams = new Hashtable();
                    foreach (string? key in queryParams.AllKeys)
                    {
                        if (!string.IsNullOrEmpty(key) && key != "api_key")
                        {
                            nextSearchParams[key] = queryParams[key];
                        }
                    }

                    // Explicitly set engine
                    nextSearchParams["engine"] = "google_maps_reviews";

                    searchParams = nextSearchParams;
                    currentPage++;
                }
                else
                {
                    Console.WriteLine("   No further pagination link returned by SerpApi. End of feed.");
                    break;
                }
            }

            // Filter by date range and sort newest first in memory
            var filteredResults = allFetchedReviews
                .Where(r => r.ReviewDate >= startDate && r.ReviewDate <= endDate)
                .OrderByDescending(r => r.ReviewDate)
                .ToList();

            Console.WriteLine($"   [Summary] Total fetched: {allFetchedReviews.Count} | Matched date range: {filteredResults.Count}");

            return filteredResults;
        }

        private static DateTime ParseRelativeDateToPacific(string relativeDate, TimeZoneInfo pacificZone)
        {
            DateTime nowPacific = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, pacificZone);
            string cleanDate = relativeDate.ToLower().Trim();

            if (cleanDate.Contains("day"))
            {
                int days = ExtractFirstNumber(cleanDate, 1);
                return nowPacific.AddDays(-days);
            }
            if (cleanDate.Contains("week"))
            {
                int weeks = ExtractFirstNumber(cleanDate, 1);
                return nowPacific.AddDays(-7 * weeks);
            }
            if (cleanDate.Contains("month"))
            {
                int months = ExtractFirstNumber(cleanDate, 1);
                return nowPacific.AddMonths(-months);
            }
            if (cleanDate.Contains("year"))
            {
                int years = ExtractFirstNumber(cleanDate, 1);
                return nowPacific.AddYears(-years);
            }

            return nowPacific; // Default fallback to current time if unparseable
        }

        private static int ExtractFirstNumber(string input, int defaultValue)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, @"\d+");
            return match.Success ? int.Parse(match.Value) : defaultValue;
        }

        // --- YELP EXTRACTOR (QUOTA PROTECTED: MAX 1-2 CALLS) ---
        private static async Task<List<ReviewRecord>> FetchYelpReviewsAsync(DateTime startDate, DateTime endDate, TimeZoneInfo pacificZone)
        {
            var results = new List<ReviewRecord>();
            string targetPlaceId = _yelpPlaceId;

            // Optional 1-call lookup if slug was passed
            if (!string.IsNullOrEmpty(targetPlaceId) && targetPlaceId.Contains("-"))
            {
                Hashtable searchLookupParams = new Hashtable
                {
                    { "engine", "yelp" },
                    { "find_desc", targetPlaceId.Replace("-", " ") }
                };

                try
                {
                    GoogleSearch lookupSearch = new GoogleSearch(searchLookupParams, _serpApiKey);
                    JObject lookupResult = lookupSearch.GetJson();
                    string? resolvedId = lookupResult["organic_results"]?[0]?["place_ids"]?[0]?.ToString();
                    if (!string.IsNullOrEmpty(resolvedId))
                    {
                        targetPlaceId = resolvedId;
                    }
                }
                catch { }
            }

            // Exactly 1 API call for reviews
            Hashtable searchParams = new Hashtable
            {
                { "engine", "yelp_reviews" },
                { "place_id", targetPlaceId },
                { "sortby", "date_desc" }
            };

            try
            {
                GoogleSearch jb = new GoogleSearch(searchParams, _serpApiKey);
                JObject result = jb.GetJson();
                var reviews = result["reviews"];

                if (reviews != null && reviews.HasValues)
                {
                    foreach (var review in reviews)
                    {
                        string dateStr = review["date"]?.ToString() ?? review["iso_date"]?.ToString() ?? "";

                        if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                        {
                            if (parsedDate > endDate || parsedDate < startDate) continue;

                            string comment = review["comment"]?["text"]?.ToString()
                                          ?? review["snippet"]?.ToString()
                                          ?? review["text"]?.ToString()
                                          ?? "";

                            results.Add(new ReviewRecord
                            {
                                Source = "Yelp",
                                GuestName = review["user"]?["name"]?.ToString() ?? "Anonymous",
                                ReviewDate = parsedDate,
                                OverallRating = review["rating"]?.ToString() ?? "0",
                                ReviewComments = comment,
                                RestaurantReply = review["owner_response"]?["text"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning during Yelp extraction: {ex.Message}");
            }

            return results;
        }

        // --- TRIPADVISOR EXTRACTOR ---
        private static async Task<List<ReviewRecord>> FetchTripAdvisorReviewsAsync(DateTime startDate, DateTime endDate, TimeZoneInfo pacificZone)
        {
            var results = new List<ReviewRecord>();

            string cleanPlaceId = _tripAdvisorLocationId.Trim();
            if (cleanPlaceId.StartsWith("d", StringComparison.OrdinalIgnoreCase))
            {
                cleanPlaceId = cleanPlaceId.Substring(1);
            }

            Hashtable searchParams = new Hashtable
            {
                { "engine", "tripadvisor_reviews" },
                { "place_id", cleanPlaceId },
                { "sort_by", "most_recent" },
                { "limit", "20" } // Maximum allowed per call
            };

            const int MaxApiRequests = 2; // Hard cap to guarantee you use at most 2 credits
            int requestCount = 0;
            int offset = 0;

            try
            {
                while (requestCount < MaxApiRequests)
                {
                    if (offset > 0)
                    {
                        searchParams["offset"] = offset.ToString();
                    }

                    requestCount++;
                    GoogleSearch jb = new GoogleSearch(searchParams, _serpApiKey);
                    JObject result = jb.GetJson();

                    if (result["error"] != null)
                    {
                        Console.WriteLine($"⚠️ TripAdvisor API Notice: {result["error"]}");
                        break;
                    }

                    var reviews = result["reviews"];
                    if (reviews == null || !reviews.HasValues)
                    {
                        break;
                    }

                    int batchCount = 0;

                    foreach (var review in reviews)
                    {
                        batchCount++;

                        // 1. CLEAN DISPLAY NAME EXTRACTION
                        string rawName = review["author"]?["name"]?.ToString()
                                      ?? review["author"]?["username"]?.ToString()
                                      ?? "Anonymous";

                        string cleanName = Regex.Replace(rawName, @"([a-z0-9])([A-Z0-9]{5,})$", "$1").Trim();

                        // 2. PRIORITIZE WRITTEN / TRIP DATE OVER PUBLICATION DATE
                        // SerpApi stores trip date inside trip_info["date"] or trip_date
                        string dateStr = review["trip_info"]?["date"]?.ToString()
                                      ?? review["trip_date"]?.ToString()
                                      ?? review["date"]?.ToString()
                                      ?? "";

                        if (DateTime.TryParse(dateStr, out DateTime parsedDate))
                        {
                            // Date boundary check against written/trip date
                            if (parsedDate > endDate || parsedDate < startDate) continue;

                            string comment = review["text"]?.ToString() ?? review["snippet"]?.ToString() ?? "";
                            string title = review["title"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(title))
                            {
                                comment = string.IsNullOrEmpty(comment) ? title : $"{title} - {comment}";
                            }

                            results.Add(new ReviewRecord
                            {
                                Source = "TripAdvisor",
                                GuestName = cleanName,
                                ReviewDate = parsedDate,
                                OverallRating = review["rating"]?.ToString() ?? "0",
                                ReviewComments = comment,
                                RestaurantReply = review["owner_response"]?["text"]?.ToString()
                                               ?? review["response"]?["text"]?.ToString()
                                               ?? ""
                            });
                        }
                    }

                    // If we received less than 20 items, there are no more pages to check
                    if (batchCount < 20)
                    {
                        break;
                    }

                    offset += batchCount;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning during TripAdvisor extraction: {ex.Message}");
            }

            return results;
        }

        // --- UNIFIED CSV EXPORTER ---
        private static async Task ExportToCsvAsync(List<ReviewRecord> records, string filePath)
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            await writer.WriteLineAsync("Source,Guest Name,Review Date,Server Name,Overall Rating,Food,Service,Atmosphere,Review Comments,Restaurant Reply");

            foreach (var r in records)
            {
                string line = $"{r.Source}," +
                             $"{EscapeForCsv(r.GuestName)}," +
                             $"{r.ReviewDate:yyyy-MM-dd}," +
                             $"{EscapeForCsv(r.ServerName)}," +
                             $"{r.OverallRating}," +
                             $"{r.FoodRating}," +
                             $"{r.ServiceRating}," +
                             $"{r.AtmosphereRating}," +
                             $"{EscapeForCsv(r.ReviewComments)}," +
                             $"{EscapeForCsv(r.RestaurantReply)}";

                await writer.WriteLineAsync(line);
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