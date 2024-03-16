import asyncio
import aiofiles
import json
import os
import re
import hashlib
import sys

from typing import Any

import mistune
from mistune.renderers.markdown import MarkdownRenderer

import semantic_kernel as sk
import semantic_kernel.connectors.ai.open_ai as sk_oai
import semantic_kernel.connectors.memory.postgres as sk_mp

import semantic_kernel.text.text_chunker as sk_tc

#region: old code

async def pull_data(kernel: sk.Kernel, path: str, suffix: str) -> dict[str, list[str]]:
    # Get all files recursively from the path that have the suffix
    files = {}
    for root, _, filenames in os.walk(path):
        for filename in filenames:
            if filename.endswith(suffix):
                full_path = os.path.join(root, filename)
                sub_path = os.path.relpath(full_path, path)
                files[sub_path] = await read_async(path=full_path)
    data = await chunk_files(files)
    return data


async def chunk_data(files: dict[str, str]) -> dict[str, list[str]]:
    # Chunk on md paragraphs
    data = {}
    for filename, text in files.items():
        data[filename] = []
        for chunk in sk_tc.split_markdown_paragraph([text], 200):
            data[filename].append(chunk)
    return data


async def chunk_files(files: dict[str, str]) -> dict[str, list[str]]:
    """Split a dictionary of files into chunks based on the first line of each file.

    Args:
        files (dict[str, str]): A dictionary of files, where the key is the filename and the value is the file's contents.

    Returns:
        dict[str, list[str]]: A dictionary of chunks, where the key is the filename and the value is a list of chunks.
    """
    parser = mistune.create_markdown(renderer="")
    chunked_files = {}
    for filename, contents in files.items():
        ast, state = parser.parse(contents)
        chunked_files[filename] = await chunk_file_ast(parser.parse(contents))
    return chunked_files


async def chunk_file_ast(ast_state: tuple[list[dict[str, str]], Any]) -> list[str]:
    """Split ast into chunks broken up by headers. Sequential headers are grouped together.
    
    Args:
        ast (list[dict[str,str]]): The AST to split into chunks.
        
    Returns:
    """
    md_renderer = MarkdownRenderer()
    ast, state = ast_state
    chunks = []
    chunk = []
    last_node_type = ""
    for node in ast[1:]:
        if node["type"] == "heading" and last_node_type != "heading":
            # Render the chunk
            if chunk:
                chunks.append(md_renderer.render_tokens(chunk, state))
            chunk = [node]
        else:
            chunk.append(node)
        
        last_node_type = node["type"]
    if chunk:
        chunks.append(md_renderer.render_tokens(chunk, state))
    return chunks

async def add_data_to_memory(kernel: sk.Kernel, data: dict[str, list[str]]) -> None:
    # Save to memory
    for filename, chunks in data.items():
        i = 1
        for chunk in chunks:
            await kernel.memory.save_information_async("chatbot_memory", id=f"{filename}_{i}", text=chunk)
            i += 1

#endregion

async def read_async(path: str) -> str:
    assert os.path.exists(path), f"File {path} does not exist"

    async with aiofiles.open(path, mode="r", encoding="UTF-8") as fp:
        content = await fp.read()
    return content

async def pull_data2(kernel: sk.Kernel, path: str, suffix: str) -> dict[str, list[tuple[str,str,str,str]]]:
    # Get all files recursively from the path that have the suffix
    files = {}
    for root, _, filenames in os.walk(path):
        for filename in filenames:
            if filename.endswith(suffix):
                full_path = os.path.join(root, filename)
                sub_path = os.path.relpath(full_path, path)
                files[sub_path] = await read_async(path=full_path)
    data = await chunk_files2(files)
    return data

async def chunk_files2(files: dict[str, str]) -> dict[str, list[tuple[str,str,str,str]]]:
    """Split a dictionary of files into chunks based on the first line of each file.

    Args:
        files (dict[str, str]): A dictionary of files, where the key is the filename and the value is the file's contents.

    Returns:
        dict[str, list[str]]: A dictionary of chunks, where the key is the filename and the value is a list of chunks.
    """
    parser = mistune.create_markdown(renderer="")
    chunked_files = {}
    for filename, contents in files.items():
        ast, state = parser.parse(contents)
        chunked_files[filename] = await chunk_file_ast2(parser.parse(contents))
    return chunked_files

async def chunk_file_ast2(ast_state: tuple[list[dict[str, str]], Any]) -> list[tuple[str,str,str,str]]:
    """Split ast into chunks broken up by headers. Sequential headers are grouped together.
    
    Args:
        ast (list[dict[str,str]]): The AST to split into chunks.
        
    Returns:
    """
    md_renderer = MarkdownRenderer()
    ast, state = ast_state
    regex_search = r"[\n]+[#]+"
    replacement = r" "
    chunks: list[tuple[str,str,str, str]] = []
    heading = []
    chunk = []
    last_node_type = ""
    for node in ast[1:]:
        if node["type"] == "heading" and last_node_type == "heading":
            heading.append(node)
            chunk.append(node)
        elif node["type"] == "heading" and last_node_type != "heading":
            # Render the chunk
            if chunk:
                chunks.append(await render_chunk_tuple(heading, chunk, state))
            heading = [node]
            chunk = [node]
        else:
            chunk.append(node)        
        last_node_type = node["type"]

    if chunk:
        chunks.append(await render_chunk_tuple(heading, chunk, state))
    return chunks

async def render_chunk_tuple(heading: list, chunk: list, state: Any) -> tuple[str, str, str, str]:
    md_renderer = MarkdownRenderer()
    regex_search = r"[\n]+[#]+"
    replacement = r" "
    heading_rendered = re.sub(
            regex_search,
            replacement,
            md_renderer.render_tokens(heading, state).lstrip(" #").rstrip(" \n")
    ).replace("  ", " ")
    text_rendered = md_renderer.render_tokens(chunk, state)
    return (
        heading_rendered,
        text_rendered,
        hashlib.md5(heading_rendered.encode()).hexdigest(),
        hashlib.md5(text_rendered.encode()).hexdigest()
    )


async def add_data_to_memory2(kernel: sk.Kernel, data: dict[str, list[tuple[str,str,str,str]]]) -> None:
    # Save to memory
    for filename, chunks in data.items():
        i = 1
        for chunk in chunks:
            additional_metadata = {
                "heading_hash": chunk[2],
                "text_hash": chunk[3]
            }
            await kernel.memory.save_information_async(
                "chatbot_memory",
                id=f"{filename}_{i}",
                text=chunk[1],
                description=chunk[0],
                additional_metadata=json.dumps(additional_metadata)
            )
            i += 1

async def main(args) -> None:
    input_folder = args.input_folder
    kernel = sk.Kernel()
    api_key, org_id = sk.openai_settings_from_dot_env()
    postgres_uri = sk.postgres_settings_from_dot_env()

    kernel.add_text_embedding_generation_service(
        "ada", sk_oai.OpenAITextEmbedding("text-embedding-3-small", api_key, org_id)
    )

    kernel.register_memory_store(memory_store=sk_mp.PostgresMemoryStore(postgres_uri, 1536, 1, 3))
    kernel.import_skill(sk_fio.FileIOSkill(), skill_name="file")
    kernel.import_skill(sk.core_skills.TextMemorySkill())

    print("Pulling data from files...")
    data = await pull_data2(kernel, input_folder, ".md")

    print("Adding data to memory...")
    await add_data_to_memory(kernel, data)

    #print("Setting up a chat (with memory!)")
    #chat_func, context = await setup_chat_with_memory(kernel)
    #
    #print("Begin chatting (type 'exit' to exit):\n")
    #chatting = True
    #while chatting:
    #    chatting = await chat(kernel, chat_func, context)


import argparse
parser = argparse.ArgumentParser(description='Build a memory of markdown files')
parser.add_argument('input_folder', type=str, help='The folder to read markdown files from')
args = parser.parse_args()
asyncio.run(main(args))

if __name__ == "__main__":
    asyncio.run(main())