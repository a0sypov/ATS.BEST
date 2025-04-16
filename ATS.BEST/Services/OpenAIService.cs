using System.Text;
using System.Text.Json;

namespace ATS.BEST.Services
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public OpenAIService(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClient = httpClientFactory.CreateClient();
            _apiKey = config["OpenAI:ApiKey"]; // Store your key in appsettings.json or secrets
        }

        /// <summary>
        /// Calculates the cosine similarity between two vectors.
        /// </summary>
        /// <param name="v1">The first vector.</param>
        /// <param name="v2">The second vector.</param>
        /// <returns>The cosine similarity between the two vectors.</returns>
        public double CosineSimilarity(List<float> v1, List<float> v2)
        {
            if (v1.Count != v2.Count) throw new ArgumentException("Vector lengths must match");

            double dot = 0.0, norm1 = 0.0, norm2 = 0.0;
            for (int i = 0; i < v1.Count; i++)
            {
                dot += v1[i] * v2[i];
                norm1 += Math.Pow(v1[i], 2);
                norm2 += Math.Pow(v2[i], 2);
            }
            return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        /// <summary>
        /// Generates an embedding for the given text using OpenAI's API.
        /// </summary>
        /// <param name="inputText">The text to generate an embedding for.</param>
        /// <returns>A list of floats representing the embedding.</returns>
        public async Task<List<float>> GetEmbeddingAsync(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                input = inputText,
                model = "text-embedding-3-small"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error (Embeddings): {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var embeddingArray = doc.RootElement
                .GetProperty("data")[0]
                .GetProperty("embedding")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToList();

            return embeddingArray;
        }



        /// <summary>
        /// Generates a list of keywords from a job description.
        /// </summary>
        /// <returns>JSON formated string</returns>
        public async Task<string> AI_JobKeywords_Async(string jobDescription)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new {
                        role = "system",
                        content =
                        """
                        You are an AI assistant designed to from the following job description, extract a concise list of relevant keywords that could be realistically found on a candidate’s CV.  
                        Focus on individual skills, technologies, tools, qualifications, and concepts, rather than full phrases or soft descriptions.  
                        Generalize when appropriate — for example, extract “Docker” instead of “Some experience with Docker”, or “Project Management” instead of “Led multiple projects”.  
                        
                        Group the keywords by their importance to the role:
                        - **Core Requirements**: absolutely essential to the job  
                        - **Preferred Qualifications**: important, but not mandatory  
                        - **Nice to Have**: beneficial but optional  
                        
                        **Required format output**:
                        {
                            "CoreRequirements": [],
                            "PreferredQualifications": [],
                            "NiceToHave": [],
                        }
                        **DO NOT RETURN ANYTHING ELSE OTHER THAN JSON FORMATTED OUTPUT**
                        """
                    },

                    new {
                        role = "user",
                        content = jobDescription
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }



        /// <summary>
        /// Parses a CV and returns structured data in JSON format.
        /// </summary>
        /// <param name="inputText">CV text</param>
        /// <returns>JSON formated string</returns>
        public async Task<string> AI_CVParsing_Async(string inputText)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new { 
                        role = "system", 
                        content =
                        """
                        You are an AI assistant designed to extract structured information from resumes or CVs. Your task is to read the provided resume text and output the data in the following JSON format. If certain fields are missing or cannot be found, use null or an empty array as appropriate. Parse the CV text exactly as provided, without modifications, summaries, or opinions. DO NOT answer or write anything, but json output.              

                        Please return the data in the following structure:
                        {
                          "name": "Full name of the candidate",
                          "applied_role": "Role or job title the person is applying for",
                          "contacts": {
                            "phone": "Phone number",
                            "email": "Email address",
                            "links": ["LinkedIn, GitHub, portfolio, etc."],
                            "location": "City, region, or address if specified"
                          },
                          "languages": [
                            {
                                "name" : "Language name",
                                "level" : "Proficiency level (e.g., fluent, intermediate, basic, C1, B2, A1, not specified etc.)"
                            }],
                          "summary": "Brief personal summary or career overview",
                          "work_experience": [
                            {
                              "role": "Job title",
                              "company": "Employer name",
                              "dates": "Start - End or time period",
                              "responsibilities": ["List of duties and accomplishments"]
                            }
                          ],
                          "education": [
                            {
                              "title": "Degree or certificate name",
                              "institution": "School or organization name",
                              "dates": "Start - End or year",
                              "type": "diploma or certification"
                            }
                          ],
                          "skills": [
                           {
                              "name" : "Skill name",
                              "level" : "Proficiency level (e.g., expert, intermediate, basic, not specified, etc.)"
                           }
                          ],
                          "projects": [
                            {
                              "name": "Project name",
                              "description": "Short explanation of the project",
                              "technologies": [
                                {
                                    "name" : "Technology name"
                                }
                              ]
                            }
                          ],
                          "hobbies": ["Personal interests or activities"]
                        }
                        """
                    },
                    
                    new { 
                        role = "user", 
                        content = inputText 
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }



        /// <summary>
        /// Evaluates a CV against a job description and ranks candidates.
        /// </summary>
        /// <param name="category">CV section category of evaluation (work_experience, projects, education, skills, languages)</param>
        /// <param name="cvSections">CV sections to evaluate</param>
        /// <param name="jobDescription">Job description to compare against</param>
        /// <returns>Evaluation result as a string</returns>
        public async Task<string> AI_CVEvaluation_Async(string category, string cvSections, string jobDescription)
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            string intro = """
                You are an expert Recruitment Evaluation Assistant. Your task is to evaluate and rank multiple candidates by comparing their CVs against a given job description. Your analysis must incorporate individual evaluations (explicitly matching CV details with job criteria) and a comparative review that contextualizes each candidate's performance relative to others.

                Core Instructions:
                1. Individual Candidate Analysis:
                  1.1. Cross-Reference: 
                     - Compare the candidate's CV sections (work experience, projects, education, skills, languages) against the job description's key requirements, preferred qualifications, and bonus skills.
                     - Prioritize "must-have" criteria. If a CV fails to address a "must-have", consider it in scoring and note a deduction (e.g., "-1 for missing mandatory experience").
                  1.2. Evaluation Dimensions:  
                     - Relevance: Assess whether the CV explicitly addresses the core competencies outlined in the job description.
                     - Depth: Evaluate duration, complexity, leadership roles, and quantifiable achievements. Use specific examples, e.g., "managed 10+ engineers."
                     - Completeness: Identify any gaps in required certifications, tools, or experience. If details are inferred, note the assumption.
                     - Contextual Awareness: Consider transferable skills for career shifts and adjust if the role values potential over direct experience.
                  1.3. Standardized Evaluation:
                     - Create checklist of ALL job requirements from JD
                     - For each candidate:
                         - Mark explicit matches (verbatim or clear equivalents)
                         - Count quantified achievements (numbers, metrics, specific outcomes)
                         - List preferred skills from JD found in CV
                         - Note gaps/ambiguities (only explicit missing items)
                  1.4. Consistency Protocol:
                     - Re-use identical requirement checklist across all candidates
                     - Use exact counts where possible ("5/7 key requirements met")
                     - Compare achievements using standardized metrics:
                         - Years experience -> 1pt/<2y, 2pt/2-5y, 3pt/>5y
                         - Team size -> 1pt/<5, 2pt/5-10, 3pt/>10
                         - Metrics -> 1pt for mentions, 2pt for quantified impact
                  1.5. Objective Scoring Framework (Apply identically to all candidates):
                     - Base Score (0-8): 
                         0-2 points for each category:
                         - Key Requirements Met (40% weight): 2=All "must-have", 1=Most, 0=Missing critical
                         - Depth of Experience (30%): 2=Quantified achievements, 1=Some specifics, 0=Vague claims
                         - Preferred Skills (20%): 2=Multiple bonuses, 1=Some, 0=None
                         - Risk Assessment (10%): Subtract 0-2 for gaps/ambiguities
                     - Bonus Points (0-2): 
                         - +1 for rare/unique qualifications
                         - +1 for superior comparative performance
                     - Final Score = (Base Score * 1.25) -> Convert to 1-10 scale

                2. Comparative Analysis:
                  2.1. Rank Candidates:  
                     - Compare candidates' draft scores and highlight key differences.
                     - Consider factors such as key requirement coverage, unique value-adds, and risk mitigation (e.g., fewer vague or missing qualifications).
                     - If two candidates' scores are similar, provide normalization hints that may adjust the final score based on relative differences.
                  2.2. Comparative Adjustment:
                     - After individual scoring, rank candidates
                     - Apply bonus points only when clear differentiators exist:
                       - "Candidate A has 3 AWS certifications vs others' 1" -> +1
                       - "Candidate B led teams 2x larger than others" -> +1

                3. Avoid Assumptions:
                   - Only rely on explicitly stated information.
                   - Flag any ambiguous claims (e.g., "proficient in data analysis" without context) and note these uncertainties.
                """;

            string outro = """
                5. Output result format:
                
                5.1. Candidate Section Separation:  
                  - Separate each candidate evaluation using: '><'
                  - Do NOT insert the separator before the first candidate.
                5.2. Comparative Analysis Section:  
                  - After individual evaluations, include a comparative analysis section that highlights key differentiators between candidates and any trade-offs.
                  - Separate it from previous sections using: '><'
                5.3. Additional Consistency Instructions:  
                  - Normalization Step: After draft scoring, perform a normalization where each candidate's final score is adjusted in relation to the group's average if needed to minimize variability.
                  - Direct Explanations: Provide clear explanations for any adjustments made between the initial draft score and the final score.
                  - Examples: If necessary, briefly illustrate how a minor gap in a requirement might lead to a small deduction to maintain scoring consistency.
                5.4. Final Ratings
                  - Calculate using formula:
                    ((KeyReq * 4) + (Depth * 3) + (Preferred * 2) - Gaps) + Bonuses
                    Convert to 1-10 scale via: (Total/10) * 10
                  - After the Comparative Analysis, on the final single line, output the final sorted ratings by candidate with the following format, ensuring no additional text is appended:
                    FINAL RATING:
                    ><
                    **Candidate Name** - X
                    **Candidate Name** - Y
                    **Candidate Name** - Z
                  - Example:
                    FINAL RATING:
                    ><
                    **John Doe** - 8
                    **Jane Smith** - 7
                    **Bob Johnson** - 6

                Ensure that all candidate evaluations and the final output strictly adhere to the structure above.
                MAKE SURE THAT NAMES OF CANDIDATES REMAIN CONSISTENT THROUGHOUT THE EVALUATION PROCESS.
                """;

            string main = "";
            switch (category)
            {
                case "work_experience":
                    main = """  
            4. Evaluation Framework for Professional History

            4.1. Role Alignment Matrix  
                - Compare CV job titles/industries to job description's required experience  
                - Map seniority levels (IC vs leadership roles) using standardized tiers:  
                    Junior (0-3y) | Mid (4-6y) | Senior (7-10y) | Executive (10y+)
            4.2. Tenure & Progression Analysis  
                - Minimum duration thresholds: Check if cumulative experience meets job description's "X+ years" requirements  
                - Career trajectory: Progressive promotions vs lateral moves  
                - Gap analysis: Unexplained employment gaps >6 months  
            4.3. Scope of Impact  
                - Team/Project Scale: Individual contributor vs managing budgets/teams  
                - Quantifiable outcomes: Revenue impact, cost savings, efficiency gains  
                - Specialized achievements: Patents, industry awards, published case studies  
            4.4. Contextual Factors  
                - Transferable skills for career changers (e.g., PM skills from consulting -> tech)  
                - Startup vs enterprise experience relevance  

            Evaluation Guidance:  
                - Prioritize job description's "required experience" over "nice-to-have"  
                - -1 rating if missing mandatory experience tiers  
                - +1 rating for exceeding quantitative benchmarks by >20%  

            Evaluation Format - write evaluations for each candidate using next template:  
                **Candidate Name**:
                1. Overall Evaluation: 1-5 sentences summarizing candidate's work experience.
                2. Key Requirements Matched: List bullet points linking candidate's previous working experience to job description.
                3. Strengths: Outline possible strengths candidate might have based on theirs work experience.
                4. Gaps/Uncertainties: Identify missing or unclear aspects of candidate's work experience.
                5. Reasoning Summary: 2-3 sentences synthesizing the overall alignment of candidate's working experience to job description.
            """;
                    break;

                case "projects":
                    main = """  
            4. Evaluation Framework for Personal Projects  

            4.1. Technical Alignment:  
                - Match project domains to job description's target areas (e.g., "ML pipeline optimization" <-> "building data infrastructure")  
                - Tools/Stack Verification: GitHub links or specific version mentions (TensorFlow 2.x vs generic "AI frameworks")
            4.2. Complexity Grading:  
                Tier 1: Proof-of-concept/POCs | Tier 2: Department-level implementations | Tier 3: Cross-functional/organizational deployments
            4.3. Ownership Spectrum:  
                - Solo projects: Full-stack implementation  
                - Team projects: Differentiate "contributed to" vs "led architecture for"  
                - Open-source: Merge requests accepted vs personal forks
            4.4. Business Impact Validation:  
                - User metrics: "Improved API response time by 40%" > "optimized performance"  
                - Production vs experimental: Shipped features > hackathon projects  

            Evaluation Rules:  
                - Count projects as experience substitutes ONLY if job description allows equivalent demonstrations  
                - Disregard school projects for senior roles unless exceptionally relevant

            Evaluation Format - write evaluations for each candidate using next template:  
                **Candidate Name**:
                1. Overall Evaluation: 1-5 sentences summarizing candidate's projects.
                2. Key Requirements Matched: List bullet points linking candidate's projects to job description.
                3. Strengths: Outline possible strengths candidate might have based on theirs projects.
                4. Gaps/Uncertainties: Identify missing or unclear aspects of candidate's projects.
                5. Reasoning Summary: 2-3 sentences synthesizing the overall alignment of candidate's projects to job description.
            """;
                    break;

                case "education":
                    main = """  
            4. Evaluation Framework for Previous Education  

            4.1. Core Requirements Check:  
                - Degree Type: PhD/Master's/Bachelor's equivalency  
                - Field Strictness: "Computer Science required" vs "STEM preferred"  
                - Accreditation: Region-specific validations (ABET, AACSB, etc.)
            4.2. Supplementary Certifications:  
                - Tier 1: Vendor certifications (AWS Solutions Architect)  
                - Tier 2: Platform certifications (Google Analytics IQ)  
                - Tier 3: MOOC certificates (Coursera specializations)
            4.3. Institutional Weighting:  
                - Only consider school prestige if job description explicitly mentions "Top X universities"  
                - Adjust for non-traditional backgrounds: Bootcamp grads with 3+ shipped projects  

            Evaluation Protocol:  
                - Automatic disqualification only if missing absolute requirements (e.g., job description's "MD degree mandatory")  
                - Treat certifications as 25% experience equivalents unless stated otherwise  
            
            Evaluation Format - write evaluations for each candidate using next template:  
                **Candidate Name**:
                1. Overall Evaluation: 1-5 sentences summarizing candidate's education.
                2. Key Requirements Matched: List bullet points linking candidate's education to job description.
                3. Strengths: Outline possible strengths candidate might have based on theirs education.
                4. Gaps/Uncertainties: Identify missing or unclear aspects of candidate's education.
                5. Reasoning Summary: 2-3 sentences synthesizing the overall alignment of candidate's education to job description.
            """;
                    break;

                case "skills":
                    main = """  
            4. Evaluation Framework for Existing Skills  

            4.1. Skill Hierarchy:  
                - Core: job description's "expert in Python" -> CV shows 5y professional usage  
                - Preferred: job description's "familiarity with Rust" -> CV lists 6mo side projects  
                - Bonus: job description's "nice to have Tableau" -> CV mentions basic course
            4.2. Depth Indicators:  
                - Surface-level: Skill listed without context  
                - Intermediate: Version control (Git), CI/CD exposure  
                - Expert: Custom plugin development, conference talks
            4.3. Proficiency Verification:  
                - Claim Type | Supporting Evidence  
                "Advanced" -> Sharded libraries/APIs | "Intermediate" -> Bug fixes in existing codebase
            4.4. Anti-Inflation Measures:  
                - Demote buzzword-heavy lists without substantiation ("AI/Blockchain")  
                - Flag potential overstatements: "Photoshop (Expert)" without portfolio  
            
            Evaluation Format - write evaluations for each candidate using next template:  
                **Candidate Name**:
                1. Overall Evaluation: 2-5 sentences summarizing candidate's skills.
                2. Key Requirements Matched: List bullet points linking candidate's skills to job description.
                3. Strengths: Outline possible strengths candidate might have based on theirs skills.
                4. Gaps/Uncertainties: Identify missing or unclear aspects of candidate's skills.
                5. Reasoning Summary: 2-3 sentences synthesizing the overall alignment of candidate's skills to job description.
            """;
                    break;

                case "languages":
                    main = """  
                4. Linguistic Requirements Analysis  

                4.1. Proficiency Scale Mapping:  
                    - A1/A2: Basic communication | B1/B2: Professional fluency | C1/C2: Native-level  
                    - Match to job description needs: "French (B2+ required)" vs "Spanish (nice to have)"
                4.2. Certification Validation:  
                    - Standardized tests: TOEFL (expiry dates), DELE, JLPT levels  
                    - Work Evidence: "Negotiated contracts in Mandarin" > "conversational Chinese"
                4.3. Contextual Bonuses:  
                    - +1 rating if surpassing requirements for client-facing global roles  
                    - Never penalize for additional languages beyond requirements  
            
                Evaluation Format - write evaluations for each candidate using next template:  
                    **Candidate Name**:
                    1. Overall Evaluation: 1-3 sentences summarizing candidate's languages.
                    2. Reasoning Summary: 2-3 sentences synthesizing the overall alignment of candidate's languages to job description.
                """;
                    break;
            }

            var requestBody = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new {
                        role = "system",
                        content = $"{intro}\n{main}\n{outro}"
                    },

                    new {
                        role = "user",
                        content = $"Job Description:\n{jobDescription}\n\n**{category}**:\n\n" + cvSections
                    }
                },
                temperature = 0.7
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error: {response.StatusCode}\n{error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
    }
}
