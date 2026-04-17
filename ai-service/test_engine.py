import models as m
from engine import CombinationEngine

def test():
    # Mock Intent: "Create me a combination of two or three matches, only wins, minimum odds 2.10"
    intent = m.NLPIntent(
        num_matches=[2, 3],
        bet_type="win",
        min_odds=2.10,
        filters=m.NLPFilters(leagues=[], min_probability=0.6)
    )

    # Sample Data
    matches = [
        m.MatchData(
            match_id="101", home_team="Arsenal", away_team="Chelsea", league="Premier League",
            odds=m.MatchOdds(home_win=1.85, away_win=3.40, draw=3.20),
            probabilities=m.MatchProbabilities(home_win=0.72, away_win=0.15, draw=0.13),
            form=m.MatchForm(home=0.8, away=0.4)
        ),
        m.MatchData(
            match_id="102", home_team="Real Madrid", away_team="Getafe", league="La Liga",
            odds=m.MatchOdds(home_win=1.35, away_win=8.00, draw=5.00),
            probabilities=m.MatchProbabilities(home_win=0.85, away_win=0.05, draw=0.10),
            form=m.MatchForm(home=0.9, away=0.3)
        ),
        m.MatchData(
            match_id="103", home_team="Bayern Munich", away_team="Bochum", league="Bundesliga",
            odds=m.MatchOdds(home_win=1.20, away_win=12.00, draw=7.00),
            probabilities=m.MatchProbabilities(home_win=0.90, away_win=0.02, draw=0.08),
            form=m.MatchForm(home=0.95, away=0.2)
        )
    ]

    engine = CombinationEngine(matches, intent)
    results = engine.run()

    print(f"Generated {len(results)} combinations.")
    for i, res in enumerate(results):
        print(f"\nCombination #{i+1} (Score: {res.score})")
        print(f"Total Odds: {res.total_odds}")
        print(f"Avg Prob: {res.avg_probability}")
        for mat in res.matches:
            print(f"  - {mat.home_team} vs {mat.away_team} | Selection: {mat.selection} | Odds: {mat.odds}")
        print(f"Reasoning: {res.reasoning}")

if __name__ == "__main__":
    test()
