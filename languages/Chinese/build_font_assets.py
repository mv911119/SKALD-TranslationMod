import csv
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Union

from PIL import Image, ImageDraw, ImageFont


# 当前脚本目录。
SCRIPT_DIR = Path(__file__).resolve()

# 项目根目录。
PROJECT_ROOT_DIR = SCRIPT_DIR.parent

# 需要扫描的文本目录。
TEXT_SOURCE_DIR = PROJECT_ROOT_DIR / "translations"

# CSV 输入统一按 UTF-8 处理。
CSV_ENCODING = "utf-8"

# 原英文字符图集目录。
ENGLISH_ATLAS_DIR = PROJECT_ROOT_DIR / "fonts_en"

# 生成的中文字符图集目录。
CHINESE_ATLAS_DIR = PROJECT_ROOT_DIR / "fonts_cn"

# 最终拼接后的完整字符图集目录。
STITCHED_ATLAS_DIR = PROJECT_ROOT_DIR / "fonts"

# 生成的 language_pack.json 输出路径。
LANGUAGE_PACK_OUTPUT = PROJECT_ROOT_DIR / "language_pack.json"

# 巨大字体文字路径
# 大字体文件路径。
HUGE_FONT_FILE_PATH = PROJECT_ROOT_DIR / "ttf/三极行楷简体-粗字体.ttf"

# 大字体文件路径。
BIG_FONT_FILE_PATH = PROJECT_ROOT_DIR / "ttf/WenQuanYi.Bitmap.Song.16px.ttf"

# 小字体文件路径。
TINY_FONT_FILE_PATH = PROJECT_ROOT_DIR / "ttf/fusion-pixel-10px-proportional-zh_hans.ttf"

# language pack 的固定元数据。
LANGUAGE_CODE = "cn"
LANGUAGE_NAME = "Chinese"
LANGUAGE_DESCRIPTION = "中文字体语言包"
LANGUAGE_VERSION = "1.0.0"
FONT_FILES_PATH = "fonts"
TRANSLATION_FILES_PATH = "translations"

# 从 90 开始与现有 language_pack 规则保持一致。
CHARACTER_INDEX_START = 90

# 这些字符已经在原始字体图集中存在，输出时会剔除。
BASE_STRING = """ !"#%&'()+,-./0123456789:;>?ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz{|}~¨‘’“”…"""


@dataclass(frozen=True)
class AtlasConfig:
    """单张中文字符图集的生成参数。"""

    filename: str                           # 文件名
    columns: int                            # 每行字符贴图数
    cell_width: int                         # 单个字符贴图的宽度
    cell_height: int                        # 单个字符贴图的高度
    font_color: tuple[int, int, int, int]   # 字体颜色 RGBA
    font_size: int                          # 字体大小
    offset_x: int                           # 字符在格子内的水平偏移（正数向右，负数向左）
    offset_y: int                           # 字符在格子内的垂直偏移（正数向下，负数向上）
    fonts: Path                             # 当前图集使用的字体文件
    grid_directions: tuple[str, ...] = ("inner", "bottom")  # 网格线配置，默认绘制内部网格线和底边框


# 可选网格方向：
# - inner: 绘制内部网格线
# - top / bottom / left / right: 绘制外边框
ATLAS_CONFIGS = [
    AtlasConfig("BigFont.png",                      9,  16, 16, (0, 0, 0, 255),         16,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("IlluminatedFont.png",              9,  16, 16, (237, 241, 113, 255),   16,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("IlluminatedFontLarge.png",         9,  20, 20, (0, 0, 0, 255),         20,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("InsularHuge.png",                  9,  20, 20, (0, 0, 0, 255),         20,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("InsularMedium.png",                9,  16, 16, (0, 0, 0, 255),         16,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("InsularTiny.png",                  9,  10, 10, (0, 0, 0, 255),         10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("MedievalHuge.png",                 9,  28, 28, (237, 241, 113, 255),   28,  0,  0,  HUGE_FONT_FILE_PATH),
    AtlasConfig("MedievalHugeThin.png",             9,  28, 28, (0, 0, 0, 255),         28,  0,  0,  HUGE_FONT_FILE_PATH),
    AtlasConfig("MedievalMedium.png",               9,  20, 20, (0, 0, 0, 255),         20,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("MediumFont.png",                   9,  16, 16, (0, 0, 0, 255),         16,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("MediumFontBlue.png",               9,  16, 16, (117, 206, 200, 255),   16,  0,  0,  BIG_FONT_FILE_PATH),
    AtlasConfig("TinyFont.png",                     9,  10, 10, (178, 178, 178, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontCapitalized.png",          9,  10, 10, (178, 178, 178, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontCapitalizedYellow.png",    9,  10, 10, (237, 241, 113, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontFat.png",                  9,  10, 10, (0, 0, 0, 255),         10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontTall.png",                 9,  10, 10, (237, 241, 113, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontTallCapitalized.png",      9,  10, 10, (237, 241, 113, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
    AtlasConfig("TinyFontYellow.png",               9,  10, 10, (237, 241, 113, 255),   10,  0,  0,  TINY_FONT_FILE_PATH),
]


def get_csv_files(root_dir: Path) -> list[Path]:
    """递归获取目录下全部 CSV 文件。"""
    return sorted(
        path
        for path in root_dir.rglob("*")
        if path.is_file() and path.suffix.lower() == ".csv" and not path.name.startswith(".")
    )


def collect_chars_from_csv(csv_path: Path, char_buffer: list[str], char_set: set[str]) -> None:
    """读取单个 CSV 的 translate 列，提取其中的字符。"""
    print(f"[CSV] 处理文件: {csv_path}")

    with csv_path.open("r", encoding=CSV_ENCODING, newline="") as file:
        rows = list(csv.reader(file))

    if not rows:
        return

    header = [column.strip().lower() for column in rows[0]]
    if "translate" not in header:
        print(f"[CSV] 跳过文件，未找到 translate 列: {csv_path}")
        return

    translate_column_index = header.index("translate")

    for row in rows[1:]:
        if len(row) <= translate_column_index:
            continue

        text = row[translate_column_index].strip()
        if not text:
            continue

        for char in text:
            if char not in char_set:
                char_set.add(char)
                char_buffer.append(char)


def collect_translation_chars(root_dir: Path) -> str:
    """从指定文本目录中收集全部去重字符，并剔除基础字符。"""
    csv_files = get_csv_files(root_dir)
    print(f"[CSV] 找到 {len(csv_files)} 个 CSV 文件")

    char_buffer: list[str] = []
    char_set: set[str] = set()

    for csv_file in csv_files:
        collect_chars_from_csv(csv_file, char_buffer, char_set)

    filtered_chars = [char for char in char_buffer if char not in BASE_STRING]
    sorted_chars = "".join(sorted(filtered_chars))
    print(f"[CSV] 提取到 {len(sorted_chars)} 个待生成字符")
    return sorted_chars


def build_character_chart(chars: str) -> dict[str, int]:
    """构建 language pack 所需的字符索引表。"""
    chart: dict[str, int] = {}
    index = CHARACTER_INDEX_START
    for char in chars:
        chart[char] = index
        index += 1
    return chart


def write_language_pack(chars: str, output_path: Path) -> None:
    """写出 language_pack.json。"""
    output_path.parent.mkdir(parents=True, exist_ok=True)

    content = {
        "languageCode": LANGUAGE_CODE,
        "name": LANGUAGE_NAME,
        "description": LANGUAGE_DESCRIPTION,
        "version": LANGUAGE_VERSION,
        "fontFilesPath": FONT_FILES_PATH,
        "translationFilesPath": TRANSLATION_FILES_PATH,
        "characterChart": build_character_chart(chars),
    }

    with output_path.open("w", encoding="utf-8") as file:
        json.dump(content, file, ensure_ascii=False, indent=2)

    print(f"[JSON] 已生成: {output_path}")


def load_font(font_path: Path, font_size: int) -> Union[ImageFont.FreeTypeFont, ImageFont.ImageFont]:
    """加载字体；若字体不存在则抛出明确错误。"""
    if not font_path.exists():
        raise FileNotFoundError(f"字体文件不存在: {font_path}")
    return ImageFont.truetype(str(font_path), size=font_size)


def parse_grid_flags(grid_directions: Iterable[str]) -> tuple[bool, bool, bool, bool, bool]:
    """把网格方向配置解析成具体布尔值。"""
    grid_set = {item.lower() for item in grid_directions}
    draw_inner_grid = "inner" in grid_set
    grid_top = "top" in grid_set
    grid_bottom = "bottom" in grid_set
    grid_left = "left" in grid_set
    grid_right = "right" in grid_set
    return draw_inner_grid, grid_top, grid_bottom, grid_left, grid_right


def generate_atlas_image(chars: str, config: AtlasConfig) -> Image.Image:
    """根据配置生成单张中文字符图集。"""
    draw_inner_grid, grid_top, grid_bottom, grid_left, grid_right = parse_grid_flags(
        config.grid_directions
    )

    total_rows = math.ceil(len(chars) / config.columns)
    inner_v_gap = 1 if draw_inner_grid else 0
    inner_h_gap = 1 if draw_inner_grid else 0
    outer_left = 1 if grid_left else 0
    outer_right = 1 if grid_right else 0
    outer_top = 1 if grid_top else 0
    outer_bottom = 1 if grid_bottom else 0

    content_width = config.columns * config.cell_width + max(config.columns - 1, 0) * inner_v_gap
    content_height = total_rows * config.cell_height + max(total_rows - 1, 0) * inner_h_gap
    atlas_width = content_width + outer_left + outer_right
    atlas_height = content_height + outer_top + outer_bottom

    atlas = Image.new("RGBA", (atlas_width, atlas_height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(atlas)
    font = load_font(config.fonts, config.font_size)

    grid_color = (255, 0, 255, 255)
    grid_x0 = outer_left
    grid_y0 = outer_top
    grid_x1 = grid_x0 + content_width - 1
    grid_y1 = grid_y0 + content_height - 1

    if draw_inner_grid:
        for column in range(1, config.columns):
            x_pos = grid_x0 + column * config.cell_width + (column - 1) * inner_v_gap
            draw.line([(x_pos, grid_y0), (x_pos, grid_y1)], fill=grid_color, width=1)

        for row in range(1, total_rows):
            y_pos = grid_y0 + row * config.cell_height + (row - 1) * inner_h_gap
            draw.line([(grid_x0, y_pos), (grid_x1, y_pos)], fill=grid_color, width=1)

    if grid_top:
        draw.line([(0, 0), (atlas_width - 1, 0)], fill=grid_color, width=1)
    if grid_bottom:
        draw.line([(0, atlas_height - 1), (atlas_width - 1, atlas_height - 1)], fill=grid_color, width=1)
    if grid_left:
        draw.line([(0, 0), (0, atlas_height - 1)], fill=grid_color, width=1)
    if grid_right:
        draw.line([(atlas_width - 1, 0), (atlas_width - 1, atlas_height - 1)], fill=grid_color, width=1)

    for index, char in enumerate(chars):
        column = index % config.columns
        row_from_bottom = index // config.columns
        visual_row = (total_rows - 1) - row_from_bottom

        cell_x = grid_x0 + column * (config.cell_width + inner_v_gap)
        cell_y = grid_y0 + visual_row * (config.cell_height + inner_h_gap)
        draw_x = cell_x + config.offset_x

        try:
            mask_core = font.getmask(char, mode="1")
            mask_img = Image.frombytes("L", mask_core.size, bytes(mask_core))
            draw_y = cell_y + (config.cell_height - mask_img.height) + config.offset_y
            colored = Image.new("RGBA", mask_img.size, config.font_color)
            atlas.paste(colored, (int(draw_x), int(draw_y)), mask_img)
        except Exception:
            text_bbox = draw.textbbox((0, 0), char, font=font)
            draw_y = cell_y + (config.cell_height - text_bbox[3]) + config.offset_y
            draw.text((draw_x, draw_y), char, font=font, fill=config.font_color)

    return atlas


def generate_chinese_atlases(chars: str, output_dir: Path) -> None:
    """批量生成中文字符图集。"""
    output_dir.mkdir(parents=True, exist_ok=True)
    print(f"[ATLAS] 开始生成中文字符图集，目标目录: {output_dir}")

    for index, config in enumerate(ATLAS_CONFIGS, start=1):
        print(f"[ATLAS] ({index}/{len(ATLAS_CONFIGS)}) 生成 {config.filename}，字体: {config.fonts}")
        image = generate_atlas_image(chars, config)
        output_path = output_dir / config.filename
        image.save(output_path, "PNG", compress_level=0)
        print(f"[ATLAS] 已保存: {output_path}")


def get_image_files(directory: Path) -> set[str]:
    """获取目录中所有图片文件名。"""
    image_extensions = {".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp"}
    return {
        file.name
        for file in directory.iterdir()
        if file.is_file() and file.suffix.lower() in image_extensions
    }


def stitch_images(head_path: Path, tail_path: Path, output_path: Path) -> None:
    """将中文字符图集作为 head、英文字符图集作为 tail 进行纵向拼接。"""
    with Image.open(head_path).convert("RGBA") as head_img:
        with Image.open(tail_path).convert("RGBA") as tail_img:
            head_width, head_height = head_img.size
            tail_width, tail_height = tail_img.size

            width_matches = head_width == tail_width
            width_matches_with_border = head_width - 1 == tail_width
            if not width_matches and not width_matches_with_border:
                raise ValueError(f"宽度不一致: head={head_width}, tail={tail_width}")

            new_width = tail_width + 1 if width_matches_with_border else tail_width
            new_height = head_height + tail_height

            stitched = Image.new("RGBA", (new_width, new_height), (0, 0, 0, 0))
            stitched.paste(head_img, (0, 0))
            stitched.paste(tail_img, (0, head_height))
            stitched.save(output_path, "PNG", compress_level=0)


def stitch_atlas_directories(english_dir: Path, chinese_dir: Path, output_dir: Path) -> None:
    """按同名文件批量拼接字符图集，中文在上，英文在下。"""
    if not english_dir.exists():
        raise FileNotFoundError(f"英文图集目录不存在: {english_dir}")
    if not chinese_dir.exists():
        raise FileNotFoundError(f"中文图集目录不存在: {chinese_dir}")

    output_dir.mkdir(parents=True, exist_ok=True)

    english_files = get_image_files(english_dir)
    chinese_files = get_image_files(chinese_dir)
    common_files = sorted(english_files & chinese_files)

    print(f"[STITCH] 英文图集 {len(english_files)} 个，中文图集 {len(chinese_files)} 个")
    print(f"[STITCH] 找到 {len(common_files)} 个同名文件可拼接")

    success_count = 0
    failed_count = 0

    for index, filename in enumerate(common_files, start=1):
        chinese_path = chinese_dir / filename
        english_path = english_dir / filename
        output_path = output_dir / filename

        print(f"[STITCH] ({index}/{len(common_files)}) 拼接 {filename}")
        try:
            stitch_images(chinese_path, english_path, output_path)
            success_count += 1
            print(f"[STITCH] 已保存: {output_path}")
        except Exception as error:
            failed_count += 1
            print(f"[STITCH] 失败: {filename} -> {error}")

    missing_in_chinese = sorted(english_files - chinese_files)
    if missing_in_chinese:
        print(f"[STITCH] 警告: 以下英文图集没有对应中文图集: {', '.join(missing_in_chinese)}")

    print(f"[STITCH] 完成: 成功 {success_count} 个, 失败 {failed_count} 个")


def main() -> None:
    """主流程：提取字符、生成图集、写 JSON、拼接图集。"""
    print("=" * 60)
    print("开始构建中文字符图集与 language_pack")
    print("=" * 60)
    print(f"文本路径：{TEXT_SOURCE_DIR}")
    chars = collect_translation_chars(TEXT_SOURCE_DIR)
    if not chars:
        raise ValueError("未提取到可用字符，请检查 CSV 目录和第三列内容。")

    write_language_pack(chars, LANGUAGE_PACK_OUTPUT)
    generate_chinese_atlases(chars, CHINESE_ATLAS_DIR)
    stitch_atlas_directories(ENGLISH_ATLAS_DIR, CHINESE_ATLAS_DIR, STITCHED_ATLAS_DIR)

    print("=" * 60)
    print(f"字符总数: {len(chars)}")
    print(f"language_pack 输出: {LANGUAGE_PACK_OUTPUT}")
    print(f"中文图集输出目录: {CHINESE_ATLAS_DIR}")
    print(f"完整图集输出目录: {STITCHED_ATLAS_DIR}")
    print("全部处理完成")
    print("=" * 60)


if __name__ == "__main__":
    main()
