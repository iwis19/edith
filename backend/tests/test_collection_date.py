from datetime import UTC, date, datetime, timedelta, timezone

from edith_backend.db import collection_date

NOW = datetime(2026, 11, 21, 12, 0, tzinfo=UTC)
EST = timezone(timedelta(hours=-5))


def test_an_evening_in_utc_minus_5_keeps_the_local_date():
    assert collection_date(datetime(2026, 11, 20, 22, 30, tzinfo=EST), NOW) == date(2026, 11, 20)


def test_a_morning_east_of_utc_keeps_the_local_date():
    assert collection_date(datetime(2026, 11, 21, 1, 0, tzinfo=timezone(timedelta(hours=9))), NOW) == date(2026, 11, 21)


def test_a_future_time_is_capped_at_now_in_its_own_offset():
    far = datetime(2099, 1, 1, tzinfo=EST)
    assert collection_date(far, NOW) == date(2026, 11, 21)
    late_now = datetime(2026, 11, 22, 3, 0, tzinfo=UTC)
    assert collection_date(far, late_now) == date(2026, 11, 21)


def test_without_a_time_the_server_utc_date_is_used():
    assert collection_date(None, datetime(2026, 11, 21, 23, 59, tzinfo=UTC)) == date(2026, 11, 21)
    assert collection_date(None, datetime(2026, 11, 21, 23, 59, tzinfo=EST)) == date(2026, 11, 22)
