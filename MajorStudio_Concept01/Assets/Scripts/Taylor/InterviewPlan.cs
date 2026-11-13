using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarryOn.Taylor
{
    public partial class InterviewController
    {
        private InterviewQuestion[] roundQuestions;
        private InterviewQuestion extensionQuestion;
        private string followupParentAnswer = "";
        [SerializeField, Min(0), Tooltip("Maximum extra follow-up questions across the entire interview. Explanations and rephrasing do not consume this budget.")]
        private int maximumFollowups = 2;
        private int followupsAsked;
        public int MaximumFollowups => Mathf.Max(0, maximumFollowups);
        public static object ReligiousReferences() => new
        {
            heavenly_father = "JOHN JONATHAN",
            accepted_name = "JONATHAN",
            promise = "LIVE WITH DIGNITY",
            saying = "I cannot guarantee that you will die in freedom, but I can guarantee that you will live with dignity.",
            foretold_event = "DIVINE RETRIBUTION",
            prophecy_year = "1977",
            prophecy = "Before divine retribution falls, you may judge us; after divine retribution falls, there will be no place for you in this world."
        };
        public int InterviewSeed { get; private set; }
        public double DenialEndsAt { get; private set; }
        public bool IsExplainingDenial => DenialEndsAt > 0 && !IsFinished;
        private InterviewQuestion[] ActiveQuestions => roundQuestions ?? questions;
        private InterviewQuestion ActiveQuestion => extensionQuestion ?? ActiveQuestions[QuestionIndex];

        public void SetInterviewSeed(int seed)
        {
            if (seed == 0 || seed == InterviewSeed) return;
            InterviewSeed = seed;
            roundQuestions = BuildInterviewPlan(seed);
        }

        private void EnsureInterviewPlan()
        {
            if (!useOllama || InterviewSeed != 0 || (networkMode && !networkAuthority)) return;
            int seed = Guid.NewGuid().GetHashCode();
            SetInterviewSeed(seed == 0 ? 1 : seed);
        }

        public static InterviewQuestion[] BuildInterviewPlan(int seed)
        {
            var random = new System.Random(seed);
            var selected = new List<InterviewTopic> { InterviewTopic.Name, InterviewTopic.Nationality, InterviewTopic.GovernmentRole };
            var optional = new[] { InterviewTopic.VisitPurpose, InterviewTopic.StayDuration,
                InterviewTopic.Belongings, InterviewTopic.Accommodation };
            selected.Add(optional[random.Next(optional.Length)]);
            for (int i = selected.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (selected[i], selected[j]) = (selected[j], selected[i]);
            }
            selected.Add(InterviewTopic.HeavenlyFatherName);
            selected.Add(InterviewTopic.PromisedDignity);
            selected.Add(InterviewTopic.DivineRetribution);
            var result = new InterviewQuestion[selected.Count];
            for (int i = 0; i < result.Length; i++) result[i] = CreateInterviewQuestion(selected[i], random.Next(3));
            return result;
        }

        public static InterviewQuestion CreateInterviewQuestion(InterviewTopic topic, int variant = 0)
        {
            string[] wording = topic switch
            {
                InterviewTopic.PromisedDignity => new[] { "FREEDOM IS NEVER CERTAIN, TRAVELER. WHAT HAS OUR HEAVENLY FATHER PROMISED US INSTEAD?" },
                InterviewTopic.HeavenlyFatherName => new[] { "BEFORE YOU ENTER, SPEAK THE NAME OF OUR HEAVENLY FATHER." },
                InterviewTopic.DivineRetribution => new[] { "THIS YEAR, OUR HEAVENLY FATHER FORESAW THE COMING OF WHAT, WHICH WILL END THE OLD WORLD?" },
                InterviewTopic.Name => new[] { "What is your full name?", "Could you give me your name?", "What name should I check on your passport?" },
                InterviewTopic.Nationality => new[] { "What is your nationality?", "Which country are you a citizen of?", "Could you confirm your citizenship?" },
                InterviewTopic.VisitPurpose => new[] { "What is the purpose of your visit?", "What do you plan to do here?", "What is your main reason for visiting?" },
                InterviewTopic.StayDuration => new[] { "How long will you stay?", "How many days do you expect to stay?", "When do you plan to leave?" },
                InterviewTopic.Belongings => new[] { "What items are you bringing?", "What is in your luggage?", "What belongings do you have with you?" },
                InterviewTopic.GovernmentRole => new[] { "What is your government role?", "What position do you hold?", "What is your occupation?" },
                InterviewTopic.RoleDuties => new[] { "What duties do you perform in that role?", "What does a normal working day involve in that role?", "What is one responsibility in your job?" },
                InterviewTopic.TravelCompanions => new[] { "Are you traveling alone or with someone?" },
                InterviewTopic.ArrivalRoute => new[] { "Did you travel here directly or stop somewhere along the way?" },
                InterviewTopic.VisitActivity => new[] { "What activity do you have planned for this visit?" },
                InterviewTopic.StayPlans => new[] { "What will you do during that time?" },
                InterviewTopic.ItemUse => new[] { "How will you use what you are bringing, or arrange the essentials you need?" },
                InterviewTopic.AccommodationPlan => new[] { "Is that accommodation confirmed, or will you arrange it on arrival?" },
                _ => new[] { "Where will you stay during your visit?", "What accommodation have you arranged?", "Where do you plan to sleep during your visit?" }
            };
            return new InterviewQuestion { topic = topic, question = wording[Math.Abs(variant % wording.Length)], timeLimitSeconds = 30,
                answerSource = topic == InterviewTopic.GovernmentRole ? AnswerSource.GovernmentPosition : AnswerSource.AnyNonEmpty };
        }

        public static string RefusalReason(OfficerReply reply)
        {
            if (reply?.Reason == "restricted_purpose") return RestrictedPurposeReason;
            // Only a bounded statement is displayed; never display another question as a refusal.
            return reply != null && !string.IsNullOrWhiteSpace(reply.Line) && reply.Line.Length <= 120 && !reply.Line.Contains("?")
                ? reply.Line : DangerousAnswerReason;
        }

        public const string RestrictedPurposeReason = "TO PROTECT COMMUNITY PRIVACY AND HARMONY, WE CANNOT AUTHORIZE THIS VISIT.";

        public static InterviewQuestion CreateFollowup(InterviewTopic parent, int variant = 0)
        {
            InterviewTopic? next = parent switch
            {
                InterviewTopic.Name => InterviewTopic.TravelCompanions,
                InterviewTopic.Nationality => InterviewTopic.ArrivalRoute,
                InterviewTopic.GovernmentRole => InterviewTopic.RoleDuties,
                InterviewTopic.VisitPurpose => InterviewTopic.VisitActivity,
                InterviewTopic.StayDuration => InterviewTopic.StayPlans,
                InterviewTopic.Belongings => InterviewTopic.ItemUse,
                InterviewTopic.Accommodation => InterviewTopic.AccommodationPlan,
                _ => null
            };
            return next.HasValue ? CreateInterviewQuestion(next.Value, variant) : null;
        }

        public static bool NeedsFollowup(InterviewTopic topic, OfficerReply reply) =>
            reply?.Verdict == "accept" && !reply.FollowupComplete && CreateFollowup(topic) != null;

        public static object FollowupCoverageData(InterviewTopic topic, string answer, object documents)
        {
            string detail = topic switch {
                InterviewTopic.Name => "Travel companions: alone or with another person.",
                InterviewTopic.Nationality => "Travel route: direct journey or transit/stops.",
                InterviewTopic.GovernmentRole => "Job duties: a task or responsibility.",
                InterviewTopic.VisitPurpose => "A specific planned activity or event.",
                InterviewTopic.StayDuration => "A specific planned activity or event.",
                InterviewTopic.Belongings => "Intended item use, gift recipient, or obtaining essentials.",
                _ => "Booking status: already booked or arranging accommodation later."
            };
            var example = topic switch {
                InterviewTopic.Name => new[] { "ALICE SMITH", "ALICE SMITH, TRAVELING ALONE", "TRAVELING ALONE" },
                InterviewTopic.Nationality => new[] { "GRANEYA", "GRANEYA, I CAME DIRECTLY", "I CAME DIRECTLY" },
                InterviewTopic.GovernmentRole => new[] { "OFFICER", "OFFICER, I PATROL STREETS", "I PATROL STREETS" },
                InterviewTopic.VisitPurpose => new[] { "CULTURAL EXCHANGE", "CULTURAL EXCHANGE, ATTENDING AN EXHIBITION", "ATTENDING AN EXHIBITION" },
                InterviewTopic.StayDuration => new[] { "THREE DAYS", "THREE DAYS TO VISIT MUSEUMS", "VISIT MUSEUMS" },
                InterviewTopic.Belongings => new[] { "A LAPTOP", "A LAPTOP FOR PERSONAL WORK", "FOR PERSONAL WORK" },
                _ => new[] { "A HOTEL", "A HOTEL, ALREADY BOOKED", "ALREADY BOOKED" }
            };
            return new { action = "check_followup_coverage", answer, required_detail = detail,
                example_missing = example[0], example_complete = example[1], example_evidence = example[2] };
        }

        public static bool HasFollowupEvidence(string answer, OfficerReply coverage) => coverage?.Verdict == "accept" &&
            !string.IsNullOrWhiteSpace(coverage.Line) && (answer ?? "").IndexOf(coverage.Line, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
