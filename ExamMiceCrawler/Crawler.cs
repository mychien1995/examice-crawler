using System.Net;
using System.Reflection;
using HtmlAgilityPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace ExamMiceCrawler;

public class Crawler
{
    private const string QuestionSelector =
        "text-stone-800 leading-relaxed space-y-4 mb-4 text-sm md:text-base break-words";

    private const string CorrectAnswerSelector = "hidden shadow-sm bg-stone-100 p-4 space-y-4 rounded-md";

    private readonly IMongoDatabase _database;

    static Crawler()
    {
        BsonClassMap.RegisterClassMap<Exam>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.MapMember(m => m.Id).SetSerializer(new GuidSerializer(BsonType.String));
        });
        BsonClassMap.RegisterClassMap<Question>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.MapMember(m => m.Id).SetSerializer(new GuidSerializer(BsonType.String));
            cm.MapMember(m => m.ExamId).SetSerializer(new GuidSerializer(BsonType.String));
        });
        BsonClassMap.RegisterClassMap<Answer>(cm =>
        {
            cm.AutoMap();
            cm.SetIgnoreExtraElements(true);
            cm.MapMember(m => m.Id).SetSerializer(new GuidSerializer(BsonType.String));
        });
    }

    public Crawler()
    {
        var mongoClient = new MongoClient("mongodb://localhost:27017/Examice");
        _database = mongoClient.GetDatabase("Examice");
    }

    public async Task Run(string examName)
    {
        Console.WriteLine($"Start crawling {examName}");
        var cookieFilePath = Path.Join(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location), "cookie.txt");
        var cookie = await File.ReadAllTextAsync(cookieFilePath);

        var examId = await GetOrCreateExam(examName);
        await Crawl(examId, cookie);
        Console.WriteLine($"Done crawling {examName}");
        Console.ReadLine();
    }

    private async Task<Guid> GetOrCreateExam(string examName)
    {
        var collection = _database.GetCollection<Exam>("exams");
        var exam = await (await collection.FindAsync(e => e.Name == examName)).FirstOrDefaultAsync();
        if (exam != null) return exam.Id;
        var id = Guid.NewGuid();
        await collection.InsertOneAsync(new Exam(id, examName, true));
        return id;
    }

    private async Task Crawl(Guid examId, string cookieValue)
    {
        var collection = _database.GetCollection<Question>("questions");
        const int batchSize = 100;

        var httpClient = new HttpClient();
        var pageIndex = 1;
        const string url = "https://examice.com/microsoft/az-104/{0}";
        var questions = new List<Question>();
        while (true)
        {
            var pageUrl = string.Format(url, pageIndex);
            var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Add("Cookie", cookieValue);
            var response = await httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"Exam content ended at page {pageIndex - 1}");
                break;
            }
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to crawl page {pageIndex}");
                break;
            }
            var responseContent = await response.Content.ReadAsStringAsync();
            var document = new HtmlDocument();
            document.LoadHtml(responseContent);
            var questionNodes = document.DocumentNode.SelectNodes($"//*[contains(@class, '{QuestionSelector}')]");
            foreach (var questionNode in questionNodes)
            {
                var questionContent = questionNode.InnerHtml;
                var answerBlock = questionNode.NextSibling;
                var anwsers = new List<Answer>();
                if (answerBlock != null)
                {
                    var selectionBlocks = answerBlock.SelectNodes(".//p");
                    foreach (var selection in selectionBlocks)
                    {
                        var selectionContent = selection.InnerHtml;
                        anwsers.Add(new Answer(Guid.NewGuid(), selectionContent));
                    }
                }

                var correctAnswerBlock =
                    questionNode.ParentNode.ParentNode.SelectNodes($".//*[contains(@class, '{CorrectAnswerSelector}')]").First();

                var correctAnswer = correctAnswerBlock.InnerHtml;

                var question = new Question(Guid.NewGuid(), examId, questionContent, anwsers, correctAnswer);
                questions.Add(question);
                if (questions.Count != batchSize) continue;
                await collection.InsertManyAsync(questions);
                Console.WriteLine($"Batch {pageIndex} inserted {questions.Count} items");
                questions = [];
            }
            pageIndex++;
        }

        if (questions.Any())
        {
            await collection.InsertManyAsync(questions);
            Console.WriteLine($"Batch {pageIndex} inserted {questions.Count} items");
        }
    }
}

public record Answer(Guid Id, string Content);
public record Exam(Guid Id, string Name, bool IsActive);
public record Question(Guid Id, Guid ExamId, string Content, List<Answer> Answers, string CorrectAnswer);