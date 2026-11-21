from edith_backend.jpeg import is_jpeg, jpeg_size

from .conftest import make_jpeg


def test_reads_the_size_past_other_segments():
    assert jpeg_size(make_jpeg(640, 480)) == (640, 480)


def test_reads_progressive_frames():
    assert jpeg_size(make_jpeg(1024, 768, sof=0xC2)) == (1024, 768)


def test_a_huffman_table_is_not_a_frame():
    dht = b"\xff\xc4" + (4).to_bytes(2, "big") + b"\x00\x00"
    data = b"\xff\xd8" + dht + make_jpeg(320, 240)[2:]
    assert jpeg_size(data) == (320, 240)


def test_truncated_or_headerless_data_has_no_size():
    assert jpeg_size(make_jpeg()[:-12]) is None
    assert jpeg_size(b"\xff\xd8\xff\xd9") is None
    assert not is_jpeg(b"GIF89a")
