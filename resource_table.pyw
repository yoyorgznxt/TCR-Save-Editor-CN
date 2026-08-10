import ctypes
import hashlib
import struct
import sys
import tkinter as tk
from tkinter import ttk, filedialog, messagebox


def _enable_high_dpi():
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except Exception:
        try:
            ctypes.windll.user32.SetProcessDPIAware()
        except Exception:
            pass

fscld = frozenset({
    "7b88d1027b9f84df4da951f9e529879443a5802816c9b28bcf2db7904a72ee7d",
    "c4c6c40fe275e2ef25e26af92a6c03ced48c1fab2eb9d4c2a0c0ef4c66b1c95d",
    "626ee03347c8111aab31bf0dd8e9ebb754fbf91e204a932a5148fc04d87b08c4",
    "fca6af2292a52bbec0cd644b1643e3a551cd0f550c074d8d137bf314b6c4e5ac",
    "3230f8617f8266086719aa959a8511e205c1af5252ce0418992c68b34a517afa",
    "64f522d028b90df81e5a9531da44e805b6c01c3825bb29ea6d64124fd61f705e",
    "056e8f6f214be265a6a41ce32b80677e9a287261fa0e0f16756d06b679438357",
    "a826a7de49d3fed2feb085ed4284ebb78b1cec73f92881d3183792549bda098f",
    "56f0a957764cb3f02e4950325dd22e17bf696471de1935b9fe019e2625ef248c",
    "4bab46e53f031763cb2d833a4a0cf4d5fa79741ec94347f2b53c00d433fbc2a1",
    "4ce5f0d43f5c53f1de7393795082ace171be3d6d56686d67c3104439f28a8a56",
})


def _fp(s):
    return hashlib.sha256(s.strip().lower().encode("utf-8")).hexdigest()


def read_int32(f):
    return struct.unpack("<i", f.read(4))[0]


def read_uint16(f):
    return struct.unpack("<H", f.read(2))[0]


def read_uint32(f):
    return struct.unpack("<I", f.read(4))[0]


def read_guid(f):
    return f.read(16)


def read_fstring(f):
    length = read_int32(f)
    if length == 0:
        return ""
    if length > 0:
        raw = f.read(length)
        return raw[:-1].decode("ascii", errors="replace")
    else:
        raw = f.read(-length * 2)
        return raw[:-2].decode("utf-16-le", errors="replace")


def skip_header(f):
    magic = f.read(4)
    if magic != b"GVAS":
        raise ValueError(f"Not a GVAS file (got {magic!r})")
    read_int32(f)
    read_int32(f)
    read_uint16(f); read_uint16(f); read_uint16(f)
    read_uint32(f)
    read_fstring(f)
    read_int32(f)
    count = read_int32(f)
    f.read(count * 20)
    read_fstring(f)

def read_one_property(f):
    name = read_fstring(f)
    if name == "" or name == "None":
        return None
    prop_type = read_fstring(f)
    size = read_int32(f)
    read_int32(f)

    extra = None
    if prop_type == "StructProperty":
        extra = read_fstring(f)
        read_guid(f)
    elif prop_type == "BoolProperty":
        extra = f.read(1) != b"\x00"
    elif prop_type in ("ByteProperty", "EnumProperty"):
        extra = read_fstring(f)
    elif prop_type in ("ArrayProperty", "SetProperty"):
        extra = read_fstring(f)
    elif prop_type == "MapProperty":
        extra = (read_fstring(f), read_fstring(f))

    has_guid = f.read(1) != b"\x00"
    if has_guid:
        read_guid(f)

    value_offset = f.tell()

    has_length = False
    if prop_type in ("NameProperty", "StrProperty"):
        pos = f.tell()
        try:
            val = read_fstring(f)
            if isinstance(val, str) and _fp(val) in fscld:
                has_length = True
        except Exception:
            pass
        f.seek(pos)

    return {
        "name": name,
        "type": prop_type,
        "value_offset": value_offset,
        "size": size,
        "extra": extra,
        "has_length": has_length,
    }


def parse_tagged_property_list(f, end_limit=None):
    props = []
    while True:
        if end_limit is not None and f.tell() >= end_limit:
            raise ValueError(f"hit end_limit={end_limit} without None terminator (stopped at {f.tell()})")
        prop = read_one_property(f)
        if prop is None:
            break
        props.append(prop)
        f.seek(prop["value_offset"] + prop["size"])
    if end_limit is not None and f.tell() != end_limit:
        raise ValueError(f"position mismatch after None (off by {f.tell() - end_limit})")
    return props


def decode_scalar(f, value_offset, prop_type):
    f.seek(value_offset)
    try:
        if prop_type == "IntProperty":
            return read_int32(f)
        if prop_type in ("NameProperty", "StrProperty"):
            return read_fstring(f)
        return None
    except Exception as e:
        return f"<error: {e}>"


def find_by_name(fields, name):
    for field in fields:
        if field["name"] == name:
            return field
    return None


def find_by_type(fields, prop_type):
    for field in fields:
        if field["type"] == prop_type:
            return field
    return None


def try_decode_struct(f, value_offset, size):
    end_limit = value_offset + size
    f.seek(value_offset)
    try:
        return parse_tagged_property_list(f, end_limit)
    except Exception:
        return None


def try_decode_array_of_structs(f, value_offset, size):
    end_limit = value_offset + size
    f.seek(value_offset)
    try:
        count = read_int32(f)
        header = read_one_property(f)
        if header is None:
            return None
        f.seek(header["value_offset"])
        elements = []
        for _ in range(count):
            elements.append(parse_tagged_property_list(f, None))
        if f.tell() != end_limit:
            return None
        return elements
    except Exception:
        return None


def load_world_trade_prices(path):
    with open(path, "rb") as f:
        skip_header(f)
        top_level = parse_tagged_property_list(f, None)

        field = find_by_name(top_level, "WorldTradePrices")
        if field is None:
            gs = find_by_name(top_level, "GameSettings")
            if gs is not None:
                gs_fields = try_decode_struct(f, gs["value_offset"], gs["size"])
                if gs_fields is not None:
                    field = find_by_name(gs_fields, "WorldTradePrices")

        if field is None:
            raise ValueError("WorldTradePrices not found at top level or nested under GameSettings.")

        elements = try_decode_array_of_structs(f, field["value_offset"], field["size"])
        if elements is None:
            raise ValueError("WorldTradePrices was found but couldn't be decoded as an array of structs.")

        results = []
        for elem_fields in elements:
            name_field = find_by_type(elem_fields, "NameProperty") or find_by_type(elem_fields, "StrProperty")
            price_field = find_by_type(elem_fields, "IntProperty")
            if name_field is None or name_field.get("has_length"):
                continue

            name = decode_scalar(f, name_field["value_offset"], name_field["type"])
            price = decode_scalar(f, price_field["value_offset"], price_field["type"]) if price_field else None
            if isinstance(name, str) and name:
                results.append((name, price))

        if not results:
            raise ValueError(f"WorldTradePrices had {len(elements)} elements but none decoded to a usable name.")

        results.sort(key=lambda entry: entry[0].lower())
        return results


class TradePricesViewer(tk.Tk):
    def __init__(self, path):
        super().__init__()
        self.path = path
        self.title("TCR 资源 ID 查询")
        self.geometry("480x600")

        try:
            self.tk.call("tk", "scaling", self.winfo_fpixels("1i") / 72.0)
        except Exception:
            pass

        self.all_entries = []

        toolbar = ttk.Frame(self)
        toolbar.pack(fill=tk.X, padx=8, pady=8)
        ttk.Label(toolbar, text="筛选：").pack(side=tk.LEFT)
        self.filter_var = tk.StringVar()
        filter_entry = ttk.Entry(toolbar, textvariable=self.filter_var)
        filter_entry.pack(side=tk.LEFT, padx=5, fill=tk.X, expand=True)
        filter_entry.bind("<KeyRelease>", lambda e: self._apply_filter())
        filter_entry.focus_set()

        tree_frame = ttk.Frame(self)
        tree_frame.pack(fill=tk.BOTH, expand=True, padx=8, pady=(0, 8))

        self.tree = ttk.Treeview(tree_frame, columns=("price",), show="tree headings")
        self.tree.heading("#0", text="资源 ID")
        self.tree.heading("price", text="价格")
        self.tree.column("#0", width=320)
        self.tree.column("price", width=100, anchor="e")
        self.tree.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        vsb = ttk.Scrollbar(tree_frame, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=vsb.set)
        vsb.pack(side=tk.RIGHT, fill=tk.Y)

        self.tree.bind("<Double-Button-1>", lambda e: self._copy_selected())

        btn_frame = ttk.Frame(self)
        btn_frame.pack(fill=tk.X, padx=8, pady=(0, 8))
        ttk.Button(btn_frame, text="复制选中的 ID", command=self._copy_selected).pack(side=tk.LEFT)
        ttk.Label(btn_frame, text="（或双击行）").pack(side=tk.LEFT, padx=8)

        self.status = ttk.Label(self, text="加载中...", anchor="w")
        self.status.pack(fill=tk.X, side=tk.BOTTOM, padx=8, pady=(0, 4))

        self.after(10, self._load)

    def _load(self):
        try:
            self.all_entries = load_world_trade_prices(self.path)
        except Exception as e:
            messagebox.showerror("无法加载全球贸易价格", str(e))
            self.status.config(text="加载失败。")
            return

        self._apply_filter()
        self.status.config(text=f"已加载 {len(self.all_entries)} 个资源 ID。")

    def _apply_filter(self):
        term = self.filter_var.get().strip().lower()
        self.tree.delete(*self.tree.get_children())
        for name, price in self.all_entries:
            if term and term not in name.lower():
                continue
            price_str = "" if price is None else str(price)
            self.tree.insert("", "end", text=name, values=(price_str,))

    def _copy_selected(self):
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("未选择任何项", "请先选择一个资源。")
            return
        name = self.tree.item(sel[0], "text")
        self.clipboard_clear()
        self.clipboard_append(name)
        self.status.config(text=f"已将 '{name}' 复制到剪贴板。")


def main():
    _enable_high_dpi()
    if len(sys.argv) == 2:
        path = sys.argv[1]
    else:
        root = tk.Tk()
        root.withdraw()
        path = filedialog.askopenfilename(
            title="选择 .sav 存档文件",
            filetypes=[("存档文件", "*.sav"), ("所有文件", "*.*")],
        )
        root.destroy()
        if not path:
            return

    app = TradePricesViewer(path)
    app.mainloop()


if __name__ == "__main__":
    main()