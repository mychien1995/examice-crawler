using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Reflection;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Serializers;

namespace ExamMiceCrawler;


public class HtmlGenerator
{
    static HtmlGenerator()
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

    private readonly IMongoDatabase _database;
    public HtmlGenerator()
    {
        var mongoClient = new MongoClient("mongodb://localhost:27017/Examice");
        _database = mongoClient.GetDatabase("Examice");
    }

    public async Task Run(string examName)
    {
        var collection = _database.GetCollection<Exam>("exams");
        var exam = await (await collection.FindAsync(e => e.Name == examName)).FirstOrDefaultAsync();
        if (exam == null)
        {
            Console.WriteLine($"Exam {examName} not found");
            return;
        }
        var questions = await _database.GetCollection<Question>("questions").Find(q => q.ExamId == exam.Id).ToListAsync();

        var htmlTemplatePath = Path.Join(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location), "template.html");
        var htmlTemplate = await File.ReadAllTextAsync(htmlTemplatePath);

        var questionBlockPath = Path.Join(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location), "question-block.html");
        var questionBlockTemplate = await File.ReadAllTextAsync(questionBlockPath);

        var htmlContentBuilder = new StringBuilder();
        for (var i = 0; i < questions.Count; i++)
        {
            var title = $"Question {i + 1}";
            var questionBlockContent = questionBlockTemplate.Replace("{{title}}", title)
                .Replace("{{content}}", questions[i].Content)
                .Replace("{{correctAnwser}}", questions[i].CorrectAnswer);
            if (questions[i].Answers.Any())
            {
                var anwserTemplate = "";
                foreach (var answer in questions[i].Answers)
                {
                    anwserTemplate += $"<p>{answer.Content}</p>";
                }
                questionBlockContent = questionBlockContent.Replace("{{answers}}", anwserTemplate);
            }
            else questionBlockContent = questionBlockContent.Replace("{{answers}}", string.Empty);

            htmlContentBuilder.Append(questionBlockContent);
            Console.WriteLine($"Processed questions {i + 1}");
        }

        var html = htmlTemplate.Replace("{{body}}", htmlContentBuilder.ToString());
        var outputFilePath = Path.Join(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location), $"{examName}-{DateTime.UtcNow:ddMMyyyyhhmmss}.html");
        await File.WriteAllTextAsync(outputFilePath, html);
        Console.WriteLine("Done");
        Console.ReadLine();
    }
}