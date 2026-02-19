import sqlite3
import os

DB_PATH = "soccer.db"
LEAGUE_ID_TO_REMOVE = 104

def cleanup_db():
    if not os.path.exists(DB_PATH):
        print(f"Error: Database not found at {DB_PATH}")
        return

    try:
        conn = sqlite3.connect(DB_PATH)
        cursor = conn.cursor()
        
        # Check count before deletion
        cursor.execute("SELECT count(*) FROM Fixtures WHERE LeagueId = ?", (LEAGUE_ID_TO_REMOVE,))
        count_before = cursor.fetchone()[0]
        print(f"Records found for LeagueId {LEAGUE_ID_TO_REMOVE} before deletion: {count_before}")
        
        if count_before == 0:
            print("No records to delete.")
            conn.close()
            return

        # Delete
        cursor.execute("DELETE FROM Fixtures WHERE LeagueId = ?", (LEAGUE_ID_TO_REMOVE,))
        deleted_count = cursor.rowcount
        conn.commit()
        
        print(f"Successfully deleted {deleted_count} records.")
        
        # Verify
        cursor.execute("SELECT count(*) FROM Fixtures WHERE LeagueId = ?", (LEAGUE_ID_TO_REMOVE,))
        count_after = cursor.fetchone()[0]
        print(f"Records remaining for LeagueId {LEAGUE_ID_TO_REMOVE}: {count_after}")
        
        if count_after == 0:
            print("Verification SUCCESS.")
        else:
            print("Verification FAILED.")

        conn.close()

    except sqlite3.Error as e:
        print(f"Database error: {e}")
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    cleanup_db()
