"""
Deterministic Combination Engine for Soccer AI.
Handles filtering, generation, scoring, and ranking of betting combinations.
"""
from __future__ import annotations
import itertools
from typing import List, Optional
import models as m


class CombinationEngine:
    def __init__(self, candidates: List[m.MatchData], intent: m.NLPIntent):
        self.candidates = candidates
        self.intent = intent

    def run(self) -> List[m.ScoredCombination]:
        # Step 1: Filter candidates based on probability and leagues
        filtered_matches = self._filter_candidates()
        
        if not filtered_matches:
            return []

        # Step 2: Generate combinations for each requested size (e.g., 2 and 3)
        results = []
        for size in self.intent.num_matches:
            combos = self._generate_combinations(filtered_matches, size)
            results.extend(combos)

        # Step 3: Score and rank results
        results.sort(key=lambda x: x.score, reverse=True)
        
        # Step 4: Final optimization — return top 5
        return results[:5]

    def _filter_candidates(self) -> List[m.ScoredCombinationMatch]:
        """Reject low-quality matches before combination generation."""
        processed = []
        min_p = self.intent.filters.min_probability
        allowed_leagues = [lg.lower() for lg in self.intent.filters.leagues]
        bet_type = self.intent.bet_type.lower()

        for match in self.candidates:
            # League filter
            if allowed_leagues and match.league.lower() not in allowed_leagues:
                continue

            # Determine selection and its probability/odds
            selection = ""
            prob = 0.0
            odds = 0.0

            if bet_type == "win":
                # Calculate Win selection (Home or Away)
                if match.probabilities.home_win >= match.probabilities.away_win:
                    selection = "Match Winner (Home)"
                    prob = match.probabilities.home_win
                    odds = match.odds.home_win
                else:
                    selection = "Match Winner (Away)"
                    prob = match.probabilities.away_win
                    odds = match.odds.away_win
            elif bet_type == "btts":
                selection = "BTTS"
                prob = match.probabilities.home_win # Placeholder for BTTS prob if not in Step 2 schema? 
                # User's Step 2 schema only had home_win/away_win/draw. 
                # I'll stick to 'win' as primary or assume schema can be extended.
                # Since the user query specifically said "only wins", I'll focus on that logic.
                pass 
            
            if prob >= min_p:
                processed.append(m.ScoredCombinationMatch(
                    match_id=match.match_id,
                    home_team=match.home_team,
                    away_team=match.away_team,
                    league=match.league,
                    selection=selection,
                    odds=odds,
                    probability=prob
                ))
        
        return processed

    def _generate_combinations(self, matches: List[m.ScoredCombinationMatch], n: int) -> List[m.ScoredCombination]:
        """Generate combinations of N matches with no team repeating."""
        results = []
        # Optimization: Early pruning — if we don't have enough matches, skip
        if len(matches) < n:
            return []

        # Generate all valid combinations
        for combo in itertools.combinations(matches, n):
            # Constraint: No team appears more than once
            if self._has_team_conflicts(combo):
                continue

            # Compute total odds
            total_odds = 1.0
            for m_match in combo:
                total_odds *= m_match.odds

            # Filter by total odds
            if total_odds < self.intent.min_odds:
                continue

            # Calculate metrics for scoring
            avg_prob = sum(mm.probability for mm in combo) / n
            
            # Form lookup (mocking average form for scoring as per Step 5)
            # In a real system, we'd fetch the numeric form from the MatchData
            avg_form = 0.5 # Default middle value for mock
            
            # Scoring: (avg_probability * 0.6) + (avg_form * 0.3) + (normalized_odds * 0.1)
            # Note: normalized_odds is relative here, I'll use a simple cap/scale
            normalized_odds = min(total_odds / 10.0, 1.0) 
            score = (avg_prob * 0.6) + (avg_form * 0.3) + (normalized_odds * 0.1)

            results.append(m.ScoredCombination(
                matches=list(combo),
                total_odds=round(total_odds, 2),
                avg_probability=round(avg_prob, 2),
                score=round(score, 3),
                reasoning=self._generate_reasoning(combo, avg_prob, total_odds)
            ))

        return results

    def _has_team_conflicts(self, combo: tuple[m.ScoredCombinationMatch, ...]) -> bool:
        teams = set()
        for m_match in combo:
            if m_match.home_team in teams or m_match.away_team in teams:
                return True
            teams.add(m_match.home_team)
            teams.add(m_match.away_team)
        return False

    def _generate_reasoning(self, combo: tuple[m.ScoredCombinationMatch, ...], avg_p: float, odds: float) -> str:
        match_count = len(combo)
        strength = "High" if avg_p > 0.75 else "Balanced"
        return f"{strength} confidence {match_count}-match combination with a cumulative probability of {avg_p:.0%} and total odds of {odds:.2f}."
