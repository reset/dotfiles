syntax enable
call plug#begin('~/.vim/plugged')
Plug 'ludovicchabant/vim-gutentags'
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
Plug 'uarun/vim-protobuf'
Plug 'PProvost/vim-ps1'
Plug 'ntpeters/vim-better-whitespace'
Plug 'Chiel92/vim-autoformat'
Plug 'mxw/vim-jsx'
Plug 'octol/vim-cpp-enhanced-highlight'
call plug#end()
set hidden
set laststatus=2
set t_Co=256
set number
set hlsearch
set colorcolumn=100
set noshowmode
set rtp+=/home/linuxbrew/.linuxbrew/opt/fzf
let g:racer_cmd="$HOME/.cargo/bin/racer"
let g:racer_experimental_completer=1
let g:ycm_rust_src_path="$HOME/.rustup/toolchains/stable-x86_64-unknown-linux-gnu/lib/rustlib/src/rust/src"
let g:rustfmt_autosave=1
let g:indentLine_char='¦'
let g:better_whitespace_eabled=1
let g:strip_whitespace_on_save=1
let g:autoformat_autoindent = 0
let g:autoformat_retab = 0
let g:autoformat_remove_trailing_spaces = 0
let base16colorspace=256
let mapleader=","
let maplocalleader=","
let NERDSpaceDelims=1
autocmd FileType javascript set tabstop=2|set shiftwidth=2|set expandtab
colorscheme base16-default-dark
nnoremap <silent> <C-l> :nohlsearch<CR><C-l>
map <F3> :YcmCompleter GoTo<CR>
map <C-p> :Files<CR>
map <leader>t :Files<CR>
map <leader>b :Buffers<CR>
map <leader>j :BTags<CR>
map <leader>J :Tags<CR>
au BufWrite * :Autoformat
