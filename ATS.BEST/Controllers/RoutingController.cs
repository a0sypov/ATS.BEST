using ATS.BEST.Services;

using Microsoft.AspNetCore.Mvc;

using UglyToad.PdfPig;

using System.Text.Json;
using static System.Collections.Specialized.BitVector32;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using static Microsoft.Extensions.Logging.EventSource.LoggingEventSource;
using System.Text.RegularExpressions;
using System.Globalization;

namespace ATS.BEST.Controllers
{
    public struct ApplicantScore
    {
        public float keywordsScore { get; set; }
        public float embeddingScore { get; set; }
        public float finalScore { get; set; }
        public float maxScore { get; set; }
        public float workExperienceScore { get; set; }
        public float projectsScore { get; set; }
        public float educationScore { get; set; }
        public float skillsScore { get; set; }
        public float languagesScore { get; set; }

        public ApplicantScore(float _keywordsScore, float _embeddingScore, float _finalScore, float _maxScore, float _workExperienceScore, float _projectsScore, float _educationScore, float _skillsScore, float _languagesScore)
        {
            keywordsScore = _keywordsScore;
            embeddingScore = _embeddingScore; 

            finalScore = _finalScore;
            maxScore = _maxScore;
            workExperienceScore = _workExperienceScore;
            projectsScore = _projectsScore;
            educationScore = _educationScore;
            skillsScore = _skillsScore;
            languagesScore = _languagesScore;
        }
    }
    public struct Applicant
    {
        public string CVFullString { get; set; }
        public CV CV { get; set; }
        public string AIEvaluation { get; set; }
        public ApplicantScore Scores { get; set; }

        public Applicant(CV _CV, string _CVFullString)
        {
            CVFullString = _CVFullString;
            CV = _CV;
            AIEvaluation = "";
            Scores = new ApplicantScore();
        }

        public Applicant(CV _CV, string _CVFullString, ApplicantScore _Score, string _AIEvaluation)
        {
            CVFullString = _CVFullString;
            CV = _CV;
            Scores = new ApplicantScore(_Score.keywordsScore, _Score.embeddingScore, _Score.finalScore, _Score.maxScore, _Score.workExperienceScore, _Score.projectsScore, _Score.educationScore, _Score.skillsScore, _Score.languagesScore);
            AIEvaluation = _AIEvaluation;
        }
    }
    public struct ApplicantEvaluation
    {
        public float score { get; set; }
        public string aiEvaluation { get; set; }

        public ApplicantEvaluation(float _score, string _aiEvaluation) : this()
        {
            this.score = _score;
            this.aiEvaluation = _aiEvaluation;
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class RoutingController : Controller
    {
        private readonly OpenAIService _openAi;
        private readonly IHubContext<ProgressHub> _hubContext;

        public RoutingController(OpenAIService openAi, IHubContext<ProgressHub> hubContext)
        {
            _openAi = openAi;
            _hubContext = hubContext;
        }

        // Helper method for retry logic
        private async Task<T> RetryOnException<T>(Func<Task<T>> operation, string progressMessage, int progressPercentage, string errorLogMessage, int maxRetries = 3)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    if (attempts > 1)
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"Retry attempt {attempts}: {progressMessage}", progressPercentage);
                    }
                    else
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress", progressMessage, progressPercentage);
                    }

                    return await operation();
                }
                catch (Exception)
                {
                    Console.WriteLine($"{errorLogMessage} (Attempt {attempts})");

                    // If reached max retries, notify failure
                    if (attempts >= maxRetries)
                    {
                        await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Maximum retry attempts reached. Operation failed.", progressPercentage);
                        throw;
                    }

                    // Wait a bit before retrying (optional)
                    await Task.Delay(100);

                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Error during processing! Retrying...", progressPercentage);
                }
            }
        }

        private Dictionary<string, string> ParseSectionsToDictionary(string[] sections)
        {
            Dictionary<string, string> sectionsDict = new Dictionary<string, string>();
            for (int i = 0; i < sections.Length - 1; i++)
            {
                int separationIndex = sections[i].IndexOf('\n');
                if (separationIndex == -1)
                    continue;
                string name = sections[i].Substring(0, separationIndex).Trim().ToLower().Replace(" ", "").Replace("*", "").Replace(":", "");
                string evaluation = sections[i].Substring(separationIndex).Trim();
                sectionsDict.Add(name, evaluation);
            }
            return sectionsDict;
        }

        private Dictionary<string, ApplicantEvaluation> ParseCandidatesEvaluation(string[] sections, int applicantsCount)
        {
            Dictionary<string, ApplicantEvaluation> ratings = new Dictionary<string, ApplicantEvaluation>();
            string[] ratingsList = sections.Last().Split('\n', StringSplitOptions.TrimEntries);

            Dictionary<string, string> sectionsDict = ParseSectionsToDictionary(sections);
            for (int i = ratingsList.Length - 1; i > ratingsList.Length - 1 - applicantsCount; i--)
            {
                string[] nameAndRating = ratingsList[i].Split('-');
                if (nameAndRating.Length != 2)
                    continue;

                string name = nameAndRating[0].Trim().ToLower().Replace(" ", "");
                float rating = 0;
                if (float.TryParse(nameAndRating[1].Trim(), out rating))
                {
                    ratings.Add(name, new ApplicantEvaluation(rating, sectionsDict[name]));
                }
            }
            return ratings;
        }

        private string ComposeApplicantsSection<T>(List<Applicant> applicants, Func<CV, IEnumerable<T>> sectionSelector, string emptySectionMessage)
        {
            return string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var sectionItems = sectionSelector(applicant.CV);
                    var entries = sectionItems.Any()
                        ? string.Join("\n", sectionItems.Select(item => item.ToString()))
                        : emptySectionMessage;
                    return $"{applicant.CV.name}:\n{entries}";
                }));
        }

        [HttpPost]
        [Route("upload")]
        public async Task<IActionResult> ConvertPDFs([FromForm] List<IFormFile> cvs, [FromForm] string jobDescription)
        {
            if (cvs == null || cvs.Count == 0)
                return BadRequest("No files uploaded.");

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Starting...", 0);

            List<Applicant> applicants = new List<Applicant>();

            string JSONJobKeywords = await _openAi.AI_JobKeywords_Async(jobDescription);
            string cleanedJSONJobKeywords = System.Text.RegularExpressions.Regex.Unescape(JSONJobKeywords);
            KeywordGroups? keywordGroups = JsonSerializer.Deserialize<KeywordGroups>(cleanedJSONJobKeywords);
            if (keywordGroups == null)
            {
                return BadRequest("Failed to parse job keywords.");
            }
            Console.WriteLine(keywordGroups.ToString());

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Prelimenary culling...", 5);
            List<float> jbEmbedding = await _openAi.GetEmbeddingAsync(jobDescription);

            int cvIndex = 1;

            try
            {
                foreach (var cv in cvs)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"CVs parsing ({cvIndex}/{cvs.Count})...", Math.Clamp(cvIndex, 10, 30));
                    cvIndex++;
                    if (cv.Length > 0)
                    {
                        using var stream = cv.OpenReadStream();
                        using var pdf = PdfDocument.Open(stream);

                        var fullText = string.Join("\n", pdf.GetPages().Select(p => p.Text));

                        List<float> cvEmbedding = await _openAi.GetEmbeddingAsync(fullText);

                        float cosineSimilarity = (float)_openAi.CosineSimilarity(jbEmbedding, cvEmbedding);

                        if (cosineSimilarity >= 0.5)
                        {
                            string JSONCV = await _openAi.AI_CVParsing_Async(fullText);
                            string cleanedJSONCV = System.Text.RegularExpressions.Regex.Unescape(JSONCV);

                            CV CV = JsonSerializer.Deserialize<CV>(cleanedJSONCV);
                            ApplicantScore newEmbeddingScore = new ApplicantScore();
                            newEmbeddingScore.embeddingScore = cosineSimilarity;

                            // applicants.Add(new Applicant(CV, fullText));
                            applicants.Add(new Applicant(CV, fullText, newEmbeddingScore, ""));
                        }
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Parsing error");
                throw;
            }
            

            for (int i = 0; i < applicants.Count; i++)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"Keywords matching ({i+1}/{applicants.Count})...", Math.Clamp(i, 35, 40));

                int coreWeight = 3;
                int preferredWeight = 2;
                int niceWeight = 1;

                float totalPossibleScore = keywordGroups.CoreRequirements.Count * coreWeight +
                                         keywordGroups.PreferredQualifications.Count * preferredWeight +
                                         keywordGroups.NiceToHave.Count * niceWeight;

                float actualScore = 0;

                // Normalize CV text
                string normalizedCv = applicants[i].CVFullString.ToLower();

                // Helper to safely create a word-boundary regex
                bool MatchesKeyword(string text, string keyword)
                {
                    string escaped = Regex.Escape(keyword.ToLower());
                    string pattern = $@"\b{escaped}\b";
                    return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
                }

                // Scoring
                actualScore += keywordGroups.CoreRequirements.Count(k => MatchesKeyword(normalizedCv, k)) * coreWeight;
                actualScore += keywordGroups.PreferredQualifications.Count(k => MatchesKeyword(normalizedCv, k)) * preferredWeight;
                actualScore += keywordGroups.NiceToHave.Count(k => MatchesKeyword(normalizedCv, k)) * niceWeight;

                float matchPercentage = actualScore / totalPossibleScore * 100;

                ApplicantScore newKeywordScore = new ApplicantScore();
                newKeywordScore.keywordsScore = matchPercentage;
                newKeywordScore.embeddingScore = applicants[i].Scores.embeddingScore;

                applicants[i] = new Applicant(applicants[i].CV, applicants[i].CVFullString, newKeywordScore, applicants[i].AIEvaluation);
            }

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"Analyzing sections...", 45);
            string workExperiences = ComposeApplicantsSection(applicants, cv => cv.work_experience, "**[NO WORK EXPERIENCE SPECIFIED]**");
            string educations = ComposeApplicantsSection(applicants, cv => cv.education, "**[NO EDUCATION SPECIFIED]**");
            string projects = ComposeApplicantsSection(applicants, cv => cv.projects, "**[NO PROJECTS SPECIFIED]**");
            string skills = ComposeApplicantsSection(applicants, cv => cv.skills, "**[NO SKILLS SPECIFIED]**");
            string languages = ComposeApplicantsSection(applicants, cv => cv.languages, "**[NO LANGUAGES SPECIFIED]**");
            
            Dictionary<string, ApplicantEvaluation> workExperienceEvaluation = await RetryOnException<Dictionary<string, ApplicantEvaluation>>(
                async () => {
                    string workExperienceEval = await _openAi.AI_CVEvaluation_Async("work_experience", workExperiences, jobDescription);
                    string[] workExperienceSections = workExperienceEval.Split(["-----"], StringSplitOptions.TrimEntries);
                    Console.WriteLine("WORK");
                    return ParseCandidatesEvaluation(workExperienceSections, applicants.Count);
                },
                "Work experience evaluation...", 50,
                "Error parsing AI evaluations for work experience"
            );

            Dictionary<string, ApplicantEvaluation> projectsEvaluation = await RetryOnException<Dictionary<string, ApplicantEvaluation>>(
                async () => {
                    string projectsEval = await _openAi.AI_CVEvaluation_Async("projects", projects, jobDescription);
                    string[] projectsSections = projectsEval.Split(["-----"], StringSplitOptions.TrimEntries);
                    Console.WriteLine("PROJECTS");
                    return ParseCandidatesEvaluation(projectsSections, applicants.Count);
                },
                "Projects evaluation...", 60,
                "Error parsing AI evaluations for projects"
            );

            Dictionary<string, ApplicantEvaluation> educationEvaluation = await RetryOnException<Dictionary<string, ApplicantEvaluation>>(
                async () => {
                    string educationEval = await _openAi.AI_CVEvaluation_Async("education", educations, jobDescription);
                    string[] educationSections = educationEval.Split(["-----"], StringSplitOptions.TrimEntries);
                    Console.WriteLine("EDUCATION");
                    return ParseCandidatesEvaluation(educationSections, applicants.Count);
                },
                "Education evaluation...", 70,
                "Error parsing AI evaluations for education"
            );

            Dictionary<string, ApplicantEvaluation> skillsEvaluation = await RetryOnException<Dictionary<string, ApplicantEvaluation>>(
                async () => {
                    string skillsEval = await _openAi.AI_CVEvaluation_Async("skills", skills, jobDescription);
                    string[] skillsSections = skillsEval.Split(["-----"], StringSplitOptions.TrimEntries);
                    Console.WriteLine("SKILLS");
                    return ParseCandidatesEvaluation(skillsSections, applicants.Count);
                },
                "Skills evaluation...", 80,
                "Error parsing AI evaluations for skills"
            );

            Dictionary<string, ApplicantEvaluation> languagesEvaluation = await RetryOnException<Dictionary<string, ApplicantEvaluation>>(
                async () => {
                    string languagesEval = await _openAi.AI_CVEvaluation_Async("languages", languages, jobDescription);
                    string[] languagesSections = languagesEval.Split(["-----"], StringSplitOptions.TrimEntries);
                    Console.WriteLine("LANGUAGES");
                    return ParseCandidatesEvaluation(languagesSections, applicants.Count);
                },
                "Languages evaluation...", 90,
                "Error parsing AI evaluations for languages"
            );

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"Finalizing results...", 95);
            for (int i = 0; i < applicants.Count; i++)
            {
                CV cv = applicants[i].CV;
                ApplicantScore newScores = new ApplicantScore();
                string applicantsName = cv.name.ToLower().Replace(" ", "");

                newScores.workExperienceScore = workExperienceEvaluation[applicantsName].score;
                newScores.projectsScore = projectsEvaluation[applicantsName].score;
                newScores.educationScore = educationEvaluation[applicantsName].score;
                newScores.skillsScore = skillsEvaluation[applicantsName].score;
                newScores.languagesScore = languagesEvaluation[applicantsName].score;

                Dictionary<string, float> sectionWeights = new Dictionary<string, float>
                    {
                        { "work_experience", 1.0f },
                        { "projects", 0.7f },
                        { "education", 0.5f },
                        { "skills", 0.3f },
                        { "languages", 0.1f }
                    };

                float finalScore = newScores.workExperienceScore * sectionWeights["work_experience"] +
                                   newScores.projectsScore * sectionWeights["projects"] +
                                   newScores.educationScore * sectionWeights["education"] +
                                   newScores.skillsScore * sectionWeights["skills"] +
                                   newScores.languagesScore * sectionWeights["languages"];
                newScores.finalScore = finalScore;

                float maxScore = 10 * sectionWeights["work_experience"] +
                                   10 * sectionWeights["projects"] +
                                   10 * sectionWeights["education"] +
                                   10 * sectionWeights["skills"] +
                                   10 * sectionWeights["languages"];

                float finalFinalScore = (applicants[i].Scores.embeddingScore * 100) * 0.4f +
                    applicants[i].Scores.keywordsScore * 0.2f + 
                    (finalScore / maxScore * 100) * 0.4f;
                newScores.maxScore = maxScore;

                newScores.finalScore = finalFinalScore;
                newScores.keywordsScore = applicants[i].Scores.keywordsScore;
                newScores.embeddingScore = applicants[i].Scores.embeddingScore;

                string AIEvaluation = $"Work experience:\n{workExperienceEvaluation[applicantsName].aiEvaluation}\n\nProjects:\n{projectsEvaluation[applicantsName].aiEvaluation}\n\nEducation:\n{educationEvaluation[applicantsName].aiEvaluation}\n\nSkills:\n{skillsEvaluation[applicantsName].aiEvaluation}\n\nLanguages:\n{languagesEvaluation[applicantsName].aiEvaluation}";
                applicants[i] = new Applicant(cv, applicants[i].CVFullString, newScores, AIEvaluation);
            }
            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Done!", 100);

            await Task.CompletedTask;
            return Ok( applicants );
        }
    }
}
