using ATS.BEST.Services;

using Microsoft.AspNetCore.Mvc;

using UglyToad.PdfPig;

using System.Text.Json;
using static System.Collections.Specialized.BitVector32;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using static Microsoft.Extensions.Logging.EventSource.LoggingEventSource;
using System.Text.RegularExpressions;

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


        [HttpPost]
        [Route("upload")]
        public async Task<IActionResult> ConvertPDFs([FromForm] List<IFormFile> cvs, [FromForm] string jobDescription)
        {
            if (cvs == null || cvs.Count == 0)
                return BadRequest("No files uploaded.");

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Starting...");

            List<Applicant> applicants = new List<Applicant>();

            string JSONJobKeywords = await _openAi.AI_JobKeywords_Async(jobDescription);
            string cleanedJSONJobKeywords = System.Text.RegularExpressions.Regex.Unescape(JSONJobKeywords);
            KeywordGroups? keywordGroups = JsonSerializer.Deserialize<KeywordGroups>(cleanedJSONJobKeywords);
            if (keywordGroups == null)
            {
                return BadRequest("Failed to parse job keywords.");
            }
            Console.WriteLine(keywordGroups.ToString());

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Prelimenary culling...");
            List<float> jbEmbedding = await _openAi.GetEmbeddingAsync(jobDescription);

            int cvIndex = 1;

            foreach (var cv in cvs)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", $"CVs parsing ({cvIndex}/{cvs.Count})...");
                cvIndex++;
                if (cv.Length > 0)
                {
                    using var stream = cv.OpenReadStream();
                    using var pdf = PdfDocument.Open(stream);

                    var fullText = string.Join("\n", pdf.GetPages().Select(p => p.Text));

                    List<float> cvEmbedding = await _openAi.GetEmbeddingAsync(fullText);

                    float cosineSimilarity = (float)_openAi.CosineSimilarity(jbEmbedding, cvEmbedding);

                    if(cosineSimilarity >= 0.5)
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

            for (int i = 0; i < applicants.Count; i++)
            {
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

            string workExperiences = string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var experiences = applicant.CV.work_experience.Any()
                        ? string.Join("\n", applicant.CV.work_experience.Select(exp => exp.ToString()))
                        : "**[NO WORK EXPERIENCE  SPECIFIED]**";
                    return $"{applicant.CV.name}:\n{experiences}";
                }));

           string educations = string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var entries = applicant.CV.education.Any()
                        ? string.Join("\n", applicant.CV.education.Select(e => e.ToString()))
                        : "**[NO EDUCATION SPECIFIED]**";
                    return $"{applicant.CV.name}:\n{entries}";
                }));

            string projects = string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var entries = applicant.CV.projects.Any()
                        ? string.Join("\n", applicant.CV.projects.Select(p => p.ToString()))
                        : "**[NO PROJECTS SPECIFIED]**";
                    return $"{applicant.CV.name}:\n{entries}";
                }));

            string skills = string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var entries = applicant.CV.skills.Any()
                        ? string.Join("\n", applicant.CV.skills.Select(s => s.ToString()))
                        : "**[NO SKILLS SPECIFIED]**";
                    return $"{applicant.CV.name}:\n{entries}";
                }));

            string languages = string.Join(
                "\n\n",
                applicants.Select(applicant =>
                {
                    var entries = applicant.CV.languages.Any()
                        ? string.Join("\n", applicant.CV.languages.Select(l => l.ToString()))
                        : "**[NO LANGUAGES SPECIFIED]**";
                    return $"{applicant.CV.name}:\n{entries}";
                }));


            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Work experience evaluation...");
            string workExperienceEval = await _openAi.AI_CVEvaluation_Async("work_experience", workExperiences, jobDescription);
            string[] workExperienceSections = workExperienceEval.Split(["-----"], StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Projects evaluation...");
            string projectsEval = await _openAi.AI_CVEvaluation_Async("projects", projects, jobDescription);
            string[] projectsSections = projectsEval.Split(["-----"], StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Education evaluation...");
            string educationEval = await _openAi.AI_CVEvaluation_Async("education", educations, jobDescription);
            string[] educationSections = educationEval.Split(["-----"], StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Skills evaluation...");
            string skillsEval = await _openAi.AI_CVEvaluation_Async("skills", skills, jobDescription);
            string[] skillsSections = skillsEval.Split(["-----"], StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Languages evaluation...");
            string languagesEval = await _openAi.AI_CVEvaluation_Async("languages", languages, jobDescription);
            string[] languagesSections = languagesEval.Split(["-----"], StringSplitOptions.TrimEntries);

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Parsing AI evaluations");

            Dictionary<string, ApplicantEvaluation> workExperienceEvaluation = new Dictionary<string, ApplicantEvaluation>();
            Dictionary<string, ApplicantEvaluation> projectsEvaluation = new Dictionary<string, ApplicantEvaluation>();
            Dictionary<string, ApplicantEvaluation> educationEvaluation = new Dictionary<string, ApplicantEvaluation>();
            Dictionary<string, ApplicantEvaluation> skillsEvaluation = new Dictionary<string, ApplicantEvaluation>();
            Dictionary<string, ApplicantEvaluation> languagesEvaluation = new Dictionary<string, ApplicantEvaluation>();

            try
            {
                Console.WriteLine("WORK");
                workExperienceEvaluation = ParseCandidatesEvaluation(workExperienceSections, applicants.Count);
                Console.WriteLine("PROJECTS");
                projectsEvaluation = ParseCandidatesEvaluation(projectsSections, applicants.Count);
                Console.WriteLine("EDUCATION");
                educationEvaluation = ParseCandidatesEvaluation(educationSections, applicants.Count);
                Console.WriteLine("SKILLS");
                skillsEvaluation = ParseCandidatesEvaluation(skillsSections, applicants.Count);
                Console.WriteLine("LANGUAGES");
                languagesEvaluation = ParseCandidatesEvaluation(languagesSections, applicants.Count);
                Console.WriteLine("AFTER");
            }
            catch (Exception)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Error during parsing!");
                Console.WriteLine("Error parsing AI evaluations");
                throw;
            }
            

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
            await _hubContext.Clients.All.SendAsync("ReceiveProgress", "Done!");

            await Task.CompletedTask;
            return Ok( applicants );
        }
    }
}
