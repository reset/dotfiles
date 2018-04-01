syntax enable
call plug#begin('~/.vim/plugged')
Plug 'ludovicchabant/vim-gutentags'
Plug '/usr/local/opt/fzf'
Plug 'junegunn/fzf.vim'
Plug 'rust-lang/rust.vim'
Plug 'racer-rust/vim-racer'
Plug 'Yggdroot/indentLine'
Plug 'jeffkreeftmeijer/vim-numbertoggle'
Plug 'vim-syntastic/syntastic'
Plug 'chriskempson/base16-vim'
Plug 'Valloric/YouCompleteMe', { 'do': './install.py --racer-completer' }
Plug 'vim-airline/vim-airline'
Plug 'scrooloose/nerdcommenter'
Plug 'cespare/vim-toml'
call plug#end()
set hidden
set laststatus=2
set t_Co=256
set number
set hlsearch
set colorcolumn=100
set noshowmode
let g:racer_cmd="/Users/reset/.cargo/bin/racer"
let g:racer_experimental_completer=1
let g:ycm_rust_src_path="/Users/reset/.rustup/toolchains/stable-x86_64-apple-darwin/lib/rustlib/src/rust/src"
let g:rustfmt_autosave=1
let g:indentLine_char='¦'
let base16colorspace=256
let mapleader=","
let maplocalleader=","
let NERDSpaceDelims=1
colorscheme base16-default-dark
nnoremap <silent> <C-l> :nohlsearch<CR><C-l>
map <F3> :YcmCompleter GoTo<CR>
map <C-p> :Files<CR>
map <leader>t :Files<CR>
map <leader>b :Buffers<CR>
map <leader>j :BTags<CR>
map <leader>J :Tags<CR>

